using System.Net;
using Microsoft.EntityFrameworkCore;
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
/// Stores a guild's ended Guild Raid seasons before the Tacticus API stops serving them. Never touches the live
/// observation, so it must not go through <see cref="GuildRaidStatusService"/>'s persistence.
/// </summary>
public sealed partial class GuildRaidSeasonCaptureService(
    PlannerDbContext db,
    ITacticusApi tacticusApi,
    IColumnHashService hashService,
    TimeProvider timeProvider,
    ILogger<GuildRaidSeasonCaptureService> logger)
{
    /// <summary>How long a run may go without starting a season before another may take over.</summary>
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

    /// <summary>Wait after a run that failed or left seasons pending.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromHours(1);

    private const int MaxNotFoundAttempts = 3;

    /// <summary>Pause before each season request.</summary>
    internal TimeSpan RequestPause { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Captures everything due for the guild, unless nothing is or another run holds it.</summary>
    public async Task RunAsync(GuildId guildId, CancellationToken ct)
    {
        var guild = await db.Guilds.AsNoTracking()
            .Where(entity => entity.Id == guildId)
            .Select(entity => new { entity.GuildApiToken, entity.TacticusGuildIdHash })
            .SingleOrDefaultAsync(ct);
        if (guild is null || string.IsNullOrWhiteSpace(guild.GuildApiToken) || guild.TacticusGuildIdHash is not { } guildHash
            || !await IsDueAsync(guildId, ct)
            || await ClaimAsync(guildId, ct) is not { } lease)
        {
            return;
        }

        var run = new Run(guildId, guild.GuildApiToken, guildHash, lease);
        var complete = false;
        try
        {
            complete = await CaptureAsync(run, ct);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            LogRunFailed(logger, guildId, exception.GetType().Name, (exception as ApiException)?.StatusCode);
        }

        // Not reached when the host stops mid-run: the lease then simply expires.
        var now = Now();
        DateTimeOffset? notBefore = complete ? null : now + RetryDelay;
        await db.GuildRaidSyncStates
            .Where(state => state.GuildId == guildId && state.CaptureNotBefore == run.Lease)
            .ExecuteUpdateAsync(setters => setters.SetProperty(state => state.CaptureNotBefore, notBefore), ct);
    }

    private async Task<bool> IsDueAsync(GuildId guildId, CancellationToken ct)
    {
        var state = await db.GuildRaidSyncStates.AsNoTracking()
            .Where(entity => entity.GuildId == guildId)
            .Select(entity => new
            {
                entity.SeasonsListedFor,
                Active = entity.ActiveSeason == null ? (int?)null : entity.ActiveSeason.SeasonNumber,
            })
            .SingleOrDefaultAsync(ct);
        return state?.Active is { } active
            && (state.SeasonsListedFor != active || await PendingBelow(guildId, active).AnyAsync(ct));
    }

    private IQueryable<GuildRaidSeasonCapture> PendingBelow(GuildId guildId, int activeSeason) =>
        db.GuildRaidSeasonCaptures.AsNoTracking().Where(capture => capture.GuildId == guildId
            && capture.Status == GuildRaidSeasonCaptureStatus.Pending
            && capture.SeasonNumber < activeSeason);

    private async Task<DateTimeOffset?> ClaimAsync(GuildId guildId, CancellationToken ct)
    {
        var now = Now();
        var lease = now + LeaseDuration;
        var claimed = await db.GuildRaidSyncStates
            .Where(state => state.GuildId == guildId && (state.CaptureNotBefore == null || state.CaptureNotBefore <= now))
            .ExecuteUpdateAsync(setters => setters.SetProperty(state => state.CaptureNotBefore, lease), ct);
        return claimed == 0 ? null : lease;
    }

    private async Task<bool> RenewAsync(Run run, CancellationToken ct)
    {
        var lease = Now() + LeaseDuration;
        var renewed = await db.GuildRaidSyncStates
            .Where(state => state.GuildId == run.GuildId && state.CaptureNotBefore == run.Lease)
            .ExecuteUpdateAsync(setters => setters.SetProperty(state => state.CaptureNotBefore, lease), ct);
        if (renewed == 0)
        {
            return false;
        }

        run.Lease = lease;
        return true;
    }

    /// <returns>Whether nothing is left to capture.</returns>
    private async Task<bool> CaptureAsync(Run run, CancellationToken ct)
    {
        // Raid responses don't name their guild, so the key is checked on every run, not only for the season list.
        var upstream = (await tacticusApi.GetGuildAsync(run.Token, ct))?.Guild;
        if (upstream is null
            || TacticusGuildIdHash.FromNullable(hashService.ComputeHash(upstream.GuildId.ToString())) is not { } hash
            || !hash.Value.AsSpan().SequenceEqual(run.GuildHash.Value))
        {
            LogGuildMismatch(logger, run.GuildId);
            return false;
        }

        var state = await db.GuildRaidSyncStates.AsNoTracking()
            .Where(entity => entity.GuildId == run.GuildId)
            .Select(entity => new
            {
                entity.SeasonsListedFor,
                Active = entity.ActiveSeason == null ? (int?)null : entity.ActiveSeason.SeasonNumber,
            })
            .SingleAsync(ct);
        if (state.Active is not { } active)
        {
            return true;
        }

        if (state.SeasonsListedFor != active)
        {
            if (upstream.GuildRaidSeasons is not { Count: > 0 } listed)
            {
                throw new InvalidOperationException("The Tacticus API listed no Guild Raid seasons.");
            }

            await ApplySeasonListAsync(run.GuildId, active, listed, ct);
        }

        var pending = await PendingBelow(run.GuildId, active)
            .OrderBy(capture => capture.SeasonNumber)
            .Select(capture => capture.SeasonNumber)
            .ToListAsync(ct);
        var complete = true;
        foreach (var season in pending)
        {
            await Task.Delay(RequestPause, timeProvider, ct);
            if (!await RenewAsync(run, ct))
            {
                return false;
            }

            complete &= await CaptureSeasonAsync(run, season, ct);
        }

        return complete;
    }

    private Task ApplySeasonListAsync(GuildId guildId, int active, List<int> listed, CancellationToken ct) =>
        InTransactionAsync(async () =>
        {
            var captures = await db.GuildRaidSeasonCaptures
                .Where(capture => capture.GuildId == guildId && capture.SeasonNumber < active)
                .ToDictionaryAsync(capture => capture.SeasonNumber, ct);
            var stored = await db.GuildRaidSeasons.AsNoTracking()
                .Where(season => season.GuildId == guildId && season.SeasonNumber < active)
                .Select(season => season.SeasonNumber)
                .ToListAsync(ct);
            var past = listed.Where(season => season > 0 && season < active).ToHashSet();

            foreach (var season in past.Union(stored).Where(season => !captures.ContainsKey(season)))
            {
                db.GuildRaidSeasonCaptures.Add(new GuildRaidSeasonCapture
                {
                    GuildId = guildId,
                    SeasonNumber = season,
                    Status = past.Contains(season)
                        ? GuildRaidSeasonCaptureStatus.Pending
                        : GuildRaidSeasonCaptureStatus.Unavailable,
                });
            }

            foreach (var capture in captures.Values)
            {
                capture.Status = (capture.Status, past.Contains(capture.SeasonNumber)) switch
                {
                    (GuildRaidSeasonCaptureStatus.Pending, false) => GuildRaidSeasonCaptureStatus.Unavailable,
                    (GuildRaidSeasonCaptureStatus.Unavailable, true) when capture.NotFoundAttempts < MaxNotFoundAttempts =>
                        GuildRaidSeasonCaptureStatus.Pending,
                    var (status, _) => status,
                };
            }

            await db.SaveChangesAsync(ct);
            await db.GuildRaidSyncStates
                .Where(state => state.GuildId == guildId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(state => state.SeasonsListedFor, active), ct);
        }, ct);

    /// <returns>Whether the season needs no further attempt.</returns>
    private async Task<bool> CaptureSeasonAsync(Run run, int seasonNumber, CancellationToken ct)
    {
        GuildRaidResponse? response;
        try
        {
            response = await tacticusApi.GetGuildRaidBySeasonAsync(run.Token, seasonNumber, ct);
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return await RecordNotFoundAsync(run.GuildId, seasonNumber, ct);
        }
        catch (ApiException exception) when ((int)exception.StatusCode is >= 200 and < 300)
        {
            // Refit's failure to deserialize a successful response.
            return await SkipUnreadableAsync(run.GuildId, seasonNumber, exception, ct);
        }

        if (response?.Entries is null)
        {
            return await SkipUnreadableAsync(run.GuildId, seasonNumber, null, ct);
        }

        if (response.Season != seasonNumber || string.IsNullOrWhiteSpace(response.SeasonConfigId))
        {
            LogSeasonMismatch(logger, run.GuildId, seasonNumber, response.Season);
            await TouchAsync(run.GuildId, seasonNumber, ct);
            return false;
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
            return await SkipUnreadableAsync(run.GuildId, seasonNumber, exception, ct);
        }

        try
        {
            var final = false;
            await InTransactionAsync(
                async () => final = await StoreSeasonAsync(run.GuildId, response, attacks, skipped, ct), ct);
            LogSeasonCaptured(logger, run.GuildId, seasonNumber, attacks.Count, skipped, final);
            return final;
        }
        catch (DbUpdateException exception)
        {
            LogSeasonNotStored(logger, exception, run.GuildId, seasonNumber);
            return false;
        }
    }

    private async Task<bool> StoreSeasonAsync(
        GuildId guildId,
        GuildRaidResponse response,
        List<GuildRaidAttack> attacks,
        int skipped,
        CancellationToken ct)
    {
        var now = Now();
        var capture = await db.GuildRaidSeasonCaptures
            .SingleAsync(entity => entity.GuildId == guildId && entity.SeasonNumber == response.Season, ct);
        capture.LastAttemptedAt = now;

        var season = await db.GuildRaidSeasons
            .SingleOrDefaultAsync(entity => entity.GuildId == guildId && entity.SeasonNumber == response.Season, ct);
        var known = season is null ? [] : await LoadIdentitiesAsync(season.Id, ct);
        if (attacks.Count == 0 && (known.Count > 0 || skipped > 0))
        {
            capture.SkippedEntryCount = skipped;
            await db.SaveChangesAsync(ct);
            return false;
        }

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

        capture.Status = GuildRaidSeasonCaptureStatus.Final;
        capture.FinalizedAt = now;
        capture.SkippedEntryCount = skipped;
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

    private async Task<bool> RecordNotFoundAsync(GuildId guildId, int seasonNumber, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var capture = await db.GuildRaidSeasonCaptures
            .SingleAsync(entity => entity.GuildId == guildId && entity.SeasonNumber == seasonNumber, ct);
        capture.LastAttemptedAt = Now();
        capture.NotFoundAttempts++;
        if (capture.NotFoundAttempts >= MaxNotFoundAttempts)
        {
            capture.Status = GuildRaidSeasonCaptureStatus.Unavailable;
        }

        await db.SaveChangesAsync(ct);
        return capture.Status == GuildRaidSeasonCaptureStatus.Unavailable;
    }

    private async Task<bool> SkipUnreadableAsync(GuildId guildId, int seasonNumber, Exception? exception, CancellationToken ct)
    {
        // The type only: an upstream exception can carry the response body, and with it player ids.
        LogSeasonUnreadable(logger, guildId, seasonNumber, exception?.GetType().Name);
        await TouchAsync(guildId, seasonNumber, ct);
        return false;
    }

    private Task<int> TouchAsync(GuildId guildId, int seasonNumber, CancellationToken ct)
    {
        var now = Now();
        return db.GuildRaidSeasonCaptures
            .Where(capture => capture.GuildId == guildId && capture.SeasonNumber == seasonNumber)
            .ExecuteUpdateAsync(setters => setters.SetProperty(capture => capture.LastAttemptedAt, now), ct);
    }

    private Task InTransactionAsync(Func<Task> work, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // Also drops the previous season, and what a retried attempt left tracked.
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await work();
            await transaction.CommitAsync(ct);
        });

    /// <summary>In the precision PostgreSQL stores, because a lease is compared with what was written.</summary>
    private DateTimeOffset Now()
    {
        var now = timeProvider.GetUtcNow();
        return new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Guild Raid season capture of guild {GuildId} stopped: {ErrorType}, upstream status {StatusCode}."
    )]
    private static partial void LogRunFailed(ILogger logger, GuildId guildId, string errorType, HttpStatusCode? statusCode);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "The stored Guild API token of guild {GuildId} answers for another guild; nothing was captured."
    )]
    private static partial void LogGuildMismatch(ILogger logger, GuildId guildId);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Guild {GuildId} asked for Guild Raid season {SeasonNumber} and got {ReturnedSeasonNumber}."
    )]
    private static partial void LogSeasonMismatch(ILogger logger, GuildId guildId, int seasonNumber, int returnedSeasonNumber);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Information,
        Message = "Guild Raid season {SeasonNumber} of guild {GuildId}: {HitCount} hits, {SkippedCount} skipped, final {IsFinal}."
    )]
    private static partial void LogSeasonCaptured(
        ILogger logger, GuildId guildId, int seasonNumber, int hitCount, int skippedCount, bool isFinal);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Error,
        Message = "Guild Raid season {SeasonNumber} of guild {GuildId} could not be stored."
    )]
    private static partial void LogSeasonNotStored(ILogger logger, Exception exception, GuildId guildId, int seasonNumber);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Error,
        Message = "Guild Raid season {SeasonNumber} of guild {GuildId} could not be read ({ErrorType}) and was skipped."
    )]
    private static partial void LogSeasonUnreadable(ILogger logger, GuildId guildId, int seasonNumber, string? errorType);

    private sealed class Run(GuildId guildId, string token, TacticusGuildIdHash guildHash, DateTimeOffset lease)
    {
        public GuildId GuildId { get; } = guildId;
        public string Token { get; } = token;
        public TacticusGuildIdHash GuildHash { get; } = guildHash;
        public DateTimeOffset Lease { get; set; } = lease;
    }

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
