using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Refit;
using TacticusPlanner.Domain.GuildRaids;
using TacticusPlanner.Domain.GuildRaids.Enums;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.TacticusApi;
using TacticusPlanner.TacticusApi.Models.GuildRaid;

namespace TacticusPlanner.Api.Features.Guilds;

/// <summary>
/// Reads a guild's ended Guild Raid season once more after a newer one started, so the hits made after its last live
/// refresh are kept. Runs inside a refresh that already succeeded, and never touches the live observation.
/// </summary>
public sealed partial class GuildRaidEndedSeasonService(
    PlannerDbContext db,
    ITacticusApi tacticusApi,
    IColumnHashService hashService,
    GuildPlayerRegistry playerRegistry,
    TimeProvider timeProvider,
    ILogger<GuildRaidEndedSeasonService> logger)
{
    /// <summary>Wait between attempts at the same ended season.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromHours(1);

    private const int MaxNotFoundAttempts = 3;

    /// <summary>
    /// Reads the guild's ended season when one is due. Never throws, except on cancellation: a failure here must not
    /// fail the refresh that called it.
    /// </summary>
    public async Task ReadIfDueAsync(GuildId guildId, string guildApiToken, CancellationToken ct)
    {
        try
        {
            if (await ClaimAsync(guildId, ct) is { } seasonNumber)
            {
                await ReadAsync(guildId, guildApiToken, seasonNumber, ct);
            }
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            // The type only: an upstream exception can carry the response body, and with it player ids.
            LogReadFailed(logger, guildId, exception.GetType().Name, (exception as ApiException)?.StatusCode);
        }
    }

    /// <returns>The ended season this call may read, or <see langword="null"/> when none is due or another call has it.</returns>
    private async Task<int?> ClaimAsync(GuildId guildId, CancellationToken ct)
    {
        var state = await db.GuildRaidSyncStates.AsNoTracking()
            .Where(entity => entity.GuildId == guildId)
            .Select(entity => new
            {
                entity.EndedSeasonNumber,
                Active = entity.ActiveSeason == null ? (int?)null : entity.ActiveSeason.SeasonNumber,
            })
            .SingleOrDefaultAsync(ct);

        // Read only once a higher season is active: that is the proof the ended one takes no more hits.
        if (state?.EndedSeasonNumber is not { } seasonNumber || state.Active is not { } active || active <= seasonNumber)
        {
            return null;
        }

        var now = Now();
        var retryBefore = now - RetryDelay;
        var claimed = await db.GuildRaidSyncStates
            .Where(entity => entity.GuildId == guildId
                && entity.EndedSeasonNumber == seasonNumber
                && (entity.EndedSeasonAttemptedAt == null || entity.EndedSeasonAttemptedAt <= retryBefore))
            .ExecuteUpdateAsync(setters => setters.SetProperty(entity => entity.EndedSeasonAttemptedAt, now), ct);
        return claimed == 0 ? null : seasonNumber;
    }

    private async Task ReadAsync(GuildId guildId, string guildApiToken, int seasonNumber, CancellationToken ct)
    {
        GuildRaidResponse? response;
        try
        {
            response = await tacticusApi.GetGuildRaidBySeasonAsync(guildApiToken, seasonNumber, ct);
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            await RecordNotFoundAsync(guildId, seasonNumber, ct);
            return;
        }

        if (response?.Entries is null)
        {
            LogSeasonUnreadable(logger, guildId, seasonNumber, null);
            return;
        }

        if (response.Season != seasonNumber || string.IsNullOrWhiteSpace(response.SeasonConfigId))
        {
            LogSeasonMismatch(logger, guildId, seasonNumber, response.Season);
            return;
        }

        var attacks = new List<GuildRaidAttack>(response.Entries.Count);
        var skipped = 0;
        try
        {
            foreach (var entry in response.Entries)
            {
                if (GuildRaidEntryNormalizer.TryBuildPastAttack(entry, hashService, out var attack))
                {
                    attacks.Add(attack);
                }
                else
                {
                    skipped++;
                }
            }
        }
        catch (Exception exception)
        {
            // Pure mapping: an unknown enum value, an impossible timestamp or a null where the contract has none.
            LogSeasonUnreadable(logger, guildId, seasonNumber, exception.GetType().Name);
            return;
        }

        try
        {
            var final = false;
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                // The refresh's tracked observation is already projected; drop it, and what a retry left tracked.
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                final = await StoreAsync(guildId, response, attacks, skipped, ct);
                await transaction.CommitAsync(ct);
            });
            LogSeasonRead(logger, guildId, seasonNumber, attacks.Count, skipped, final);
        }
        catch (DbUpdateException exception)
        {
            LogSeasonNotStored(logger, exception, guildId, seasonNumber);
        }
    }

    /// <returns>Whether the season became final.</returns>
    private async Task<bool> StoreAsync(
        GuildId guildId,
        GuildRaidResponse response,
        List<GuildRaidAttack> attacks,
        int skipped,
        CancellationToken ct)
    {
        var season = await db.GuildRaidSeasons
            .SingleOrDefaultAsync(entity => entity.GuildId == guildId && entity.SeasonNumber == response.Season, ct);
        var known = season is null ? [] : await LoadIdentitiesAsync(season.Id, ct);

        // An empty answer for a season with hits, or one whose every entry was malformed, is not believed.
        if (attacks.Count == 0 && (known.Count > 0 || skipped > 0))
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        if (season is null)
        {
            season = new GuildRaidSeason
            {
                Id = GuildRaidSeasonId.From(Guid.CreateVersion7()),
                GuildId = guildId,
                SeasonNumber = response.Season,
                SeasonConfigId = response.SeasonConfigId,
            };
            db.GuildRaidSeasons.Add(season);
        }

        season.SeasonConfigId = response.SeasonConfigId;
        season.ObservedAt = now;
        season.FinalizedAt = now;
        var matched = new Dictionary<string, GuildRaidAttack>(StringComparer.Ordinal);
        foreach (var attack in attacks)
        {
            if (known.Add(HitIdentity.Of(attack)))
            {
                attack.GuildRaidSeasonId = season.Id;
                db.GuildRaidAttacks.Add(attack);
            }
            else
            {
                matched.TryAdd(Convert.ToHexString(attack.TacticusUserIdHash.Value), attack);
            }
        }

        await db.SaveChangesAsync(ct);

        // One player has one suffix, so it is filled per player rather than hit by hit.
        if (matched.Count > 0
            && await db.GuildRaidAttacks.AnyAsync(
                attack => attack.GuildRaidSeasonId == season.Id && attack.TacticusUserIdSuffix == null, ct))
        {
            foreach (var attack in matched.Values)
            {
                await db.GuildRaidAttacks
                    .Where(stored => stored.GuildRaidSeasonId == season.Id
                        && stored.TacticusUserIdHash == attack.TacticusUserIdHash
                        && stored.TacticusUserIdSuffix == null)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(stored => stored.TacticusUserIdSuffix, attack.TacticusUserIdSuffix),
                        ct);
            }
        }

        await playerRegistry.AddAsync(
            guildId, attacks.Select(attack => (attack.TacticusUserIdHash, attack.TacticusUserIdSuffix)), ct);
        await ClearAsync(guildId, response.Season, ct);
        return true;
    }

    private async Task<HashSet<HitIdentity>> LoadIdentitiesAsync(GuildRaidSeasonId seasonId, CancellationToken ct)
    {
        // Everything but the units: they are the bulk of a hit and not part of its identity.
        var stored = await db.GuildRaidAttacks.AsNoTracking()
            .Where(attack => attack.GuildRaidSeasonId == seasonId)
            .Select(attack => new
            {
                attack.TacticusUserIdHash,
                attack.Tier,
                attack.Set,
                attack.EncounterIndex,
                attack.EncounterType,
                attack.UnitSetId,
                attack.Difficulty,
                attack.RemainingHp,
                attack.MaximumHp,
                attack.DamageDealt,
                attack.DamageType,
                attack.StartedAt,
                attack.CompletedAt,
            })
            .ToListAsync(ct);
        return stored
            .Select(hit => new HitIdentity(
                Convert.ToHexString(hit.TacticusUserIdHash.Value), hit.Tier, hit.Set, hit.EncounterIndex,
                hit.EncounterType, hit.UnitSetId, hit.Difficulty, hit.RemainingHp, hit.MaximumHp, hit.DamageDealt,
                hit.DamageType, hit.StartedAt, hit.CompletedAt))
            .ToHashSet();
    }

    private async Task RecordNotFoundAsync(GuildId guildId, int seasonNumber, CancellationToken ct)
    {
        // Conditional on the season: a newer season may have replaced it meanwhile.
        var pointed = db.GuildRaidSyncStates
            .Where(entity => entity.GuildId == guildId && entity.EndedSeasonNumber == seasonNumber);
        await pointed.ExecuteUpdateAsync(
            setters => setters.SetProperty(
                entity => entity.EndedSeasonNotFoundCount, entity => entity.EndedSeasonNotFoundCount + 1),
            ct);
        if (await pointed.Where(entity => entity.EndedSeasonNotFoundCount >= MaxNotFoundAttempts)
                .ExecuteUpdateAsync(Clear, ct) > 0)
        {
            LogSeasonGone(logger, guildId, seasonNumber);
        }
    }

    private Task<int> ClearAsync(GuildId guildId, int seasonNumber, CancellationToken ct) =>
        db.GuildRaidSyncStates
            .Where(entity => entity.GuildId == guildId && entity.EndedSeasonNumber == seasonNumber)
            .ExecuteUpdateAsync(Clear, ct);

    private static void Clear(UpdateSettersBuilder<GuildRaidSyncState> setters) => setters
        .SetProperty(entity => entity.EndedSeasonNumber, (int?)null)
        .SetProperty(entity => entity.EndedSeasonNotFoundCount, 0)
        .SetProperty(entity => entity.EndedSeasonAttemptedAt, (DateTimeOffset?)null);

    /// <summary>In the precision PostgreSQL stores, because the attempt time is compared with what was written.</summary>
    private DateTimeOffset Now()
    {
        var now = timeProvider.GetUtcNow();
        return new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Reading the ended Guild Raid season of guild {GuildId} failed: {ErrorType}, upstream status {StatusCode}."
    )]
    private static partial void LogReadFailed(ILogger logger, GuildId guildId, string errorType, HttpStatusCode? statusCode);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Guild Raid season {SeasonNumber} of guild {GuildId} is no longer served; its stored hits are kept."
    )]
    private static partial void LogSeasonGone(ILogger logger, GuildId guildId, int seasonNumber);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Guild {GuildId} asked for Guild Raid season {SeasonNumber} and got {ReturnedSeasonNumber}."
    )]
    private static partial void LogSeasonMismatch(ILogger logger, GuildId guildId, int seasonNumber, int returnedSeasonNumber);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Information,
        Message = "Ended Guild Raid season {SeasonNumber} of guild {GuildId}: {HitCount} hits, {SkippedCount} skipped, final {IsFinal}."
    )]
    private static partial void LogSeasonRead(
        ILogger logger, GuildId guildId, int seasonNumber, int hitCount, int skippedCount, bool isFinal);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Error,
        Message = "Ended Guild Raid season {SeasonNumber} of guild {GuildId} could not be stored."
    )]
    private static partial void LogSeasonNotStored(ILogger logger, Exception exception, GuildId guildId, int seasonNumber);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Error,
        Message = "Ended Guild Raid season {SeasonNumber} of guild {GuildId} could not be read ({ErrorType})."
    )]
    private static partial void LogSeasonUnreadable(ILogger logger, GuildId guildId, int seasonNumber, string? errorType);

    /// <summary>
    /// What makes two reports the same hit. Unlike the content hash it leaves out the upstream config hash, which a
    /// later response may report differently.
    /// </summary>
    private sealed record HitIdentity(
        string UserIdHash,
        int Tier,
        int Set,
        int EncounterIndex,
        GuildRaidEncounterType EncounterType,
        string UnitSetId,
        GuildRaidDifficulty Difficulty,
        int RemainingHp,
        int MaximumHp,
        int DamageDealt,
        GuildRaidDamageType DamageType,
        DateTimeOffset? StartedAt,
        DateTimeOffset CompletedAt)
    {
        public static HitIdentity Of(GuildRaidAttack attack) => new(
            Convert.ToHexString(attack.TacticusUserIdHash.Value), attack.Tier, attack.Set, attack.EncounterIndex,
            attack.EncounterType, attack.UnitSetId, attack.Difficulty, attack.RemainingHp, attack.MaximumHp,
            attack.DamageDealt, attack.DamageType, attack.StartedAt, attack.CompletedAt);
    }
}
