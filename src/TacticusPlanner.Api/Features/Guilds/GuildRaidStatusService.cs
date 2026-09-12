using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Refit;
using TacticusPlanner.Domain.GuildRaids;
using TacticusPlanner.Domain.GuildRaids.Enums;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.TacticusApi;
using TacticusPlanner.TacticusApi.Models.GuildRaid;
using TacticusPlanner.TacticusApi.Models.Shared;

namespace TacticusPlanner.Api.Features.Guilds;

public sealed class GuildRaidStatusService(
    PlannerDbContext db,
    ITacticusApi tacticusApi,
    IColumnHashService hashService,
    IGameCatalogProvider catalogProvider,
    GuildRaidRefreshCoordinator coordinator,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan CooldownWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Pure read: always returns the latest persisted observation and never calls upstream.
    /// </summary>
    public async Task<GuildRaidRefreshResult> GetAsync(Guild guild, CancellationToken ct)
    {
        var retained = await LoadRetainedAsync(guild.Id, ct);
        if (retained?.SyncState.ObservedAt is not { } observedAt)
        {
            return new GuildRaidRefreshResult.NeverObserved();
        }

        return Project(retained, guild, FreshnessOf(retained.SyncState, observedAt));
    }

    /// <summary>
    /// Performs (or reuses, within the per-guild cooldown) an upstream refresh and returns the resulting status.
    /// Dispatches through <see cref="GuildRaidRefreshCoordinator"/> so concurrent callers for the same guild
    /// join one shared, request-independent operation instead of racing the cooldown check individually.
    /// </summary>
    public Task<GuildRaidRefreshResult> RefreshAsync(Guild guild, CancellationToken ct) =>
        coordinator.RunAsync(guild, ct);

    /// <summary>
    /// The actual refresh operation, invoked by <see cref="GuildRaidRefreshCoordinator"/> inside its own
    /// isolated scope. The cooldown check is evaluated here — inside the shared flight — rather than by each
    /// caller beforehand, so a caller cannot observe a cooldown window opened by another caller's
    /// still-in-flight attempt and short-circuit instead of joining it.
    /// </summary>
    internal async Task<GuildRaidRefreshResult> RefreshCoreAsync(Guild guild, CancellationToken ct)
    {
        var retained = await LoadRetainedAsync(guild.Id, ct);
        var now = timeProvider.GetUtcNow();
        if (retained is not null && now - retained.SyncState.LastAttemptedAt < CooldownWindow)
        {
            return retained.SyncState.ObservedAt is not { } observedAt
                ? new GuildRaidRefreshResult.Unavailable("The Tacticus Guild Raid API is currently unavailable.")
                : Project(retained, guild, FreshnessOf(retained.SyncState, observedAt));
        }

        return await DoRefreshAsync(guild, retained, ct);
    }

    private static GuildRaidFreshness FreshnessOf(GuildRaidSyncState syncState, DateTimeOffset observedAt) =>
        syncState.LastAttemptedAt <= observedAt ? GuildRaidFreshness.Fresh : GuildRaidFreshness.Stale;

    private async Task<GuildRaidRefreshResult> DoRefreshAsync(
        Guild guild,
        RetainedObservation? retained,
        CancellationToken ct)
    {
        var attemptedAt = timeProvider.GetUtcNow();
        await RecordAttemptAsync(guild.Id, attemptedAt, ct);

        GuildRaidResponse response;
        try
        {
            response = await tacticusApi.GetGuildRaidsAsync(guild.GuildApiToken!, ct);
        }
        catch (ApiException exception) when ((int)exception.StatusCode is 400 or 401 or 403 or 404)
        {
            return new GuildRaidRefreshResult.Rejected("The Tacticus API rejected the stored Guild API token.");
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException or TaskCanceledException)
        {
            return retained?.SyncState.ObservedAt is null
                ? new GuildRaidRefreshResult.Unavailable("The Tacticus Guild Raid API is currently unavailable.")
                : Project(retained, guild, GuildRaidFreshness.Stale);
        }

        try
        {
            var persisted = await PersistAsync(guild.Id, response, attemptedAt, ct);
            return Project(persisted, guild, GuildRaidFreshness.Fresh);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var converged = await LoadRetainedAsync(guild.Id, ct);
            return converged?.SyncState.ObservedAt is not { } convergedObservedAt
                ? new GuildRaidRefreshResult.Unavailable("The Guild Raid observation could not be persisted.")
                : Project(converged, guild, FreshnessOf(converged.SyncState, convergedObservedAt));
        }
        catch (InvalidOperationException exception)
        {
            return new GuildRaidRefreshResult.Rejected(exception.Message);
        }
    }

    /// <summary>
    /// Records that a refresh was attempted, independent of its outcome, so the cooldown reflects failed attempts too.
    /// </summary>
    private async Task RecordAttemptAsync(GuildId guildId, DateTimeOffset attemptedAt, CancellationToken ct)
    {
        var state = await db.GuildRaidSyncStates.FirstOrDefaultAsync(entity => entity.GuildId == guildId, ct);
        if (state is null)
        {
            state = new GuildRaidSyncState
            {
                GuildId = guildId,
                State = GuildRaidObservationState.NoActiveSeason,
                LastAttemptedAt = attemptedAt,
            };
            db.GuildRaidSyncStates.Add(state);
        }
        else
        {
            state.LastAttemptedAt = attemptedAt;
        }

        await db.SaveChangesAsync(ct);
    }

    private GuildRaidRefreshResult Project(
        RetainedObservation retained,
        Guild guild,
        GuildRaidFreshness freshness)
    {
        try
        {
            return new GuildRaidRefreshResult.Success(GuildRaidStatusProjector.Project(
                retained.SyncState,
                retained.Season,
                catalogProvider.Current,
                guild.LastSyncSucceededAt!.Value,
                freshness));
        }
        catch (InvalidOperationException exception)
        {
            return new GuildRaidRefreshResult.Rejected(exception.Message);
        }
    }

    private async Task<RetainedObservation?> LoadRetainedAsync(GuildId guildId, CancellationToken ct)
    {
        var state = await db.GuildRaidSyncStates.AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.GuildId == guildId, ct);
        if (state is null)
        {
            return null;
        }

        GuildRaidSeason? season = null;
        if (state.ActiveSeasonId is { } seasonId)
        {
            season = await db.GuildRaidSeasons.AsNoTracking()
                .Include(entity => entity.Attacks)
                .FirstOrDefaultAsync(entity => entity.Id == seasonId, ct);
        }

        return new RetainedObservation(state, season);
    }

    private async Task<RetainedObservation> PersistAsync(
        GuildId guildId,
        GuildRaidResponse response,
        DateTimeOffset observedAt,
        CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            return await PersistCoreAsync(guildId, response, observedAt, ct);
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var persisted = await PersistCoreAsync(guildId, response, observedAt, ct);
            await transaction.CommitAsync(ct);
            return persisted;
        });
    }

    private async Task<RetainedObservation> PersistCoreAsync(
        GuildId guildId,
        GuildRaidResponse response,
        DateTimeOffset observedAt,
        CancellationToken ct)
    {
        var state = await db.GuildRaidSyncStates.FirstOrDefaultAsync(entity => entity.GuildId == guildId, ct);
        state ??= new GuildRaidSyncState { GuildId = guildId, State = GuildRaidObservationState.NoActiveSeason };
        if (db.Entry(state).State == EntityState.Detached)
        {
            db.GuildRaidSyncStates.Add(state);
        }

        state.ObservedAt = observedAt;
        state.LastAttemptedAt = observedAt;
        if (response.Season <= 0 || string.IsNullOrWhiteSpace(response.SeasonConfigId))
        {
            state.State = GuildRaidObservationState.NoActiveSeason;
            state.ActiveSeasonId = null;
            await db.SaveChangesAsync(ct);
            return new RetainedObservation(state, null);
        }

        if (!catalogProvider.Current.RaidBossesView.Seasons.TryGetValue(response.SeasonConfigId, out var config))
        {
            throw new InvalidOperationException(
                $"The Tacticus API returned unknown Guild Raid config '{response.SeasonConfigId}'.");
        }

        var season = await db.GuildRaidSeasons.Include(entity => entity.Attacks)
            .ThenInclude(attack => attack.Units)
            .FirstOrDefaultAsync(entity => entity.GuildId == guildId && entity.SeasonNumber == response.Season, ct);
        season ??= new GuildRaidSeason
        {
            Id = GuildRaidSeasonId.From(Guid.CreateVersion7()),
            GuildId = guildId,
            SeasonNumber = response.Season,
            SeasonConfigId = response.SeasonConfigId,
        };
        if (db.Entry(season).State == EntityState.Detached)
        {
            db.GuildRaidSeasons.Add(season);
        }
        season.SeasonConfigId = response.SeasonConfigId;
        season.ObservedAt = observedAt;

        var existingAttacks = season.Attacks.ToDictionary(attack => attack.ContentHash, StringComparer.Ordinal);
        foreach (var entry in response.Entries)
        {
            Validate(entry);
            var contentHash = BuildContentHash(entry);
            var (unitSetId, explicitProgressionIndex) = ParseUnitId(entry.UnitId);
            var progressionIndex = ResolveProgressionIndex(config, entry, unitSetId, explicitProgressionIndex);
            if (existingAttacks.TryGetValue(contentHash, out var existingAttack))
            {
                existingAttack.ProgressionIndex = progressionIndex;
                continue;
            }

            var attack = new GuildRaidAttack
            {
                Id = GuildRaidAttackId.From(Guid.CreateVersion7()),
                ContentHash = contentHash,
                TacticusUserIdHash = TacticusUserIdHash.From(hashService.ComputeHash(entry.UserId.ToString())!),
                Tier = entry.Tier,
                Set = entry.Set,
                EncounterIndex = entry.EncounterIndex,
                RemainingHp = entry.RemainingHp,
                MaximumHp = entry.MaxHp,
                EncounterType = entry.EncounterType switch
                {
                    EncounterType.SideBoss => GuildRaidEncounterType.SideBoss,
                    EncounterType.Boss => GuildRaidEncounterType.Boss,
                    _ => throw new InvalidOperationException($"Unknown Guild Raid encounter type '{entry.EncounterType}'."),
                },
                UnitSetId = unitSetId,
                ProgressionIndex = progressionIndex,
                Difficulty = entry.Rarity switch
                {
                    Rarity.Common => GuildRaidDifficulty.Common,
                    Rarity.Uncommon => GuildRaidDifficulty.Uncommon,
                    Rarity.Rare => GuildRaidDifficulty.Rare,
                    Rarity.Epic => GuildRaidDifficulty.Epic,
                    Rarity.Legendary => GuildRaidDifficulty.Legendary,
                    Rarity.Mythic => GuildRaidDifficulty.Mythic,
                    _ => throw new InvalidOperationException($"Unknown Guild Raid rarity '{entry.Rarity}'."),
                },
                DamageDealt = entry.DamageDealt,
                DamageType = entry.DamageType switch
                {
                    DamageType.Bomb => GuildRaidDamageType.Bomb,
                    DamageType.Battle => GuildRaidDamageType.Battle,
                    _ => throw new InvalidOperationException($"Unknown Guild Raid damage type '{entry.DamageType}'."),
                },
                StartedAt = entry.StartedOn is { } started
                    ? DateTimeOffset.FromUnixTimeSeconds(started)
                    : null,
                CompletedAt = DateTimeOffset.FromUnixTimeSeconds(entry.CompletedOn!.Value),
            };
            for (var index = 0; index < entry.HeroDetails.Count; index++)
            {
                attack.Units.Add(BuildUnit(
                    attack.Id,
                    entry.HeroDetails[index],
                    GuildRaidAttackUnitKind.Hero,
                    index));
            }
            if (entry.MachineOfWarDetails is { } mow)
            {
                attack.Units.Add(BuildUnit(attack.Id, mow, GuildRaidAttackUnitKind.MachineOfWar, 0));
            }
            season.Attacks.Add(attack);
            existingAttacks.Add(contentHash, attack);
        }

        state.State = GuildRaidObservationState.Active;
        state.ActiveSeasonId = season.Id;
        await db.SaveChangesAsync(ct);
        return new RetainedObservation(state, season);
    }

    private static GuildRaidAttackUnit BuildUnit(
        GuildRaidAttackId attackId,
        GuildRaidUnit unit,
        GuildRaidAttackUnitKind kind,
        int position) => new()
        {
            Id = GuildRaidAttackUnitId.From(Guid.CreateVersion7()),
            GuildRaidAttackId = attackId,
            UnitId = unit.UnitId,
            Kind = kind,
            Position = position,
            Power = unit.Power,
        };

    private static void Validate(GuildRaidEntry entry)
    {
        if (entry.UserId == Guid.Empty || entry.CompletedOn is null || string.IsNullOrWhiteSpace(entry.UnitId)
            || entry.RemainingHp < 0 || entry.MaxHp < 0)
        {
            throw new InvalidOperationException("The Tacticus API returned an invalid Guild Raid entry.");
        }
    }

    private static (string UnitSetId, int? ProgressionIndex) ParseUnitId(string value)
    {
        var separator = value.LastIndexOf(':');
        if (separator < 0)
        {
            return (value, null);
        }

        if (!int.TryParse(
                value[(separator + 1)..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var progressionIndex)
            || progressionIndex <= 0)
        {
            throw new InvalidOperationException($"The Tacticus API returned invalid Guild Raid unit id '{value}'.");
        }

        return (value[..separator], progressionIndex);
    }

    private static int ResolveProgressionIndex(
        GameCatalogRaidBossSeasonView config,
        GuildRaidEntry entry,
        string unitSetId,
        int? explicitProgressionIndex)
    {
        var encounter = config.Tiers
            .Where(tier => tier.Tier == entry.Tier)
            .SelectMany(tier => tier.Sets)
            .Where(set => set.Set == entry.Set)
            .SelectMany(set => set.Encounters)
            .FirstOrDefault(candidate => candidate.EncounterIndex == entry.EncounterIndex
                && candidate.UnitSetId == unitSetId)
            ?? throw new InvalidOperationException(
                $"The Tacticus API returned Guild Raid encounter '{unitSetId}' outside its season config position.");

        if (explicitProgressionIndex is { } sourceProgressionIndex
            && sourceProgressionIndex != encounter.ProgressionIndex)
        {
            throw new InvalidOperationException(
                $"The Tacticus API returned Guild Raid progression '{sourceProgressionIndex}' where the season config expects '{encounter.ProgressionIndex}'.");
        }

        return encounter.ProgressionIndex;
    }

    private static string BuildContentHash(GuildRaidEntry entry)
    {
        var canonical = string.Join('|',
            entry.UserId,
            entry.Tier,
            entry.Set,
            entry.EncounterIndex,
            entry.UnitId,
            entry.EncounterType,
            entry.RemainingHp,
            entry.MaxHp,
            entry.DamageDealt,
            entry.DamageType,
            entry.StartedOn,
            entry.CompletedOn,
            entry.GlobalConfigHash);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private sealed record RetainedObservation(GuildRaidSyncState SyncState, GuildRaidSeason? Season);
}

public abstract record GuildRaidRefreshResult
{
    public sealed record Success(GuildRaidStatusResponse Response) : GuildRaidRefreshResult;
    public sealed record NeverObserved : GuildRaidRefreshResult;
    public sealed record Rejected(string Message) : GuildRaidRefreshResult;
    public sealed record Unavailable(string Message) : GuildRaidRefreshResult;
}
