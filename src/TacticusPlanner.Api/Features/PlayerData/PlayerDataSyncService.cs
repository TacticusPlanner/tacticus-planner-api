using Microsoft.EntityFrameworkCore;
using Npgsql;
using Refit;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;
using TacticusPlanner.TacticusApi;
using PlayerDataSnapshotEntity = TacticusPlanner.Domain.PlayerData.PlayerDataSnapshot;

namespace TacticusPlanner.Api.Features.PlayerData;

/// <summary>
/// Fetches the current player from the Tacticus API, transforms it, and persists it as a
/// <see cref="PlayerDataSnapshotEntity"/> — the sync logic <see cref="PlayerSyncEndpoint"/> exposes
/// directly, extracted so <c>ImportV1ProfileEndpoint</c> can also call it: a V1 import that selects
/// goals needs a snapshot to exist before <c>V1GoalImportService</c> runs, not on whatever later
/// request happens to trigger the player-data provider's own sync (rewrite-v1-goal-import's
/// player_data_required refusal was otherwise near-guaranteed on a brand-new account's first import).
/// </summary>
public sealed class PlayerDataSyncService(
    PlannerDbContext db,
    ITacticusApi tacticusApi,
    PlayerDataTransformer transformer,
    TimeProvider timeProvider)
{
    private static readonly IReadOnlyDictionary<string, Action<PlayerDataSnapshotEntity, PlayerDataTransformResult>> ChunkSetters =
        new Dictionary<string, Action<PlayerDataSnapshotEntity, PlayerDataTransformResult>>(StringComparer.Ordinal)
        {
            [PlayerDataChunkKeys.PlayerDetails] = (s, t) => s.PlayerDetails = t.PlayerDetails,
            [PlayerDataChunkKeys.Characters] = (s, t) => s.Characters = t.Characters,
            [PlayerDataChunkKeys.Mows] = (s, t) => s.Mows = t.Mows,
            [PlayerDataChunkKeys.InventoryUpgrades] = (s, t) => s.InventoryUpgrades = t.InventoryUpgrades,
            [PlayerDataChunkKeys.InventoryItems] = (s, t) => s.InventoryItems = t.InventoryItems,
            [PlayerDataChunkKeys.InventoryShards] = (s, t) => s.InventoryShards = t.InventoryShards,
            [PlayerDataChunkKeys.Inventory] = (s, t) => s.Inventory = t.Inventory,
            [PlayerDataChunkKeys.CampaignProgress] = (s, t) => s.CampaignProgress = t.CampaignProgress,
            [PlayerDataChunkKeys.CampaignEventsProgress] = (s, t) => s.CampaignEventsProgress = t.CampaignEventsProgress,
            [PlayerDataChunkKeys.LiveProgress] = (s, t) => s.LiveProgress = t.LiveProgress,
            [PlayerDataChunkKeys.LreProgress] = (s, t) => s.LreProgress = t.LreProgress,
        };

    public async Task<PlayerDataSyncResult> SyncAsync(ProfileId profileId, string apiKey, CancellationToken ct)
    {
        var integration = await db.TacticusIntegrations.FirstOrDefaultAsync(entity => entity.Id == profileId, ct);
        if (integration is not null)
        {
            integration.TacticusSyncLastAttemptedAt = timeProvider.GetUtcNow();
        }

        TacticusApi.Models.Player.PlayerResponse response;
        try
        {
            response = await tacticusApi.GetPlayerAsync(apiKey, ct);
        }
        catch (ApiException exception) when ((int)exception.StatusCode is 400 or 401 or 403 or 404)
        {
            await db.SaveChangesAsync(ct);
            return new PlayerDataSyncResult.UpstreamRejected();
        }

        var transformed = transformer.Transform(response);
        var syncedAt = timeProvider.GetUtcNow();

        var snapshot = await db.PlayerDataSnapshots.FirstOrDefaultAsync(entity => entity.Id == profileId, ct);
        var isNew = snapshot is null;
        snapshot ??= new PlayerDataSnapshotEntity { Id = profileId };
        ApplyTransform(snapshot, transformed, isNew, syncedAt);

        if (isNew)
        {
            db.PlayerDataSnapshots.Add(snapshot);
        }

        if (integration is not null)
        {
            integration.TacticusSyncLastSucceededAt = syncedAt;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (isNew && IsSnapshotPrimaryKeyConflict(exception))
        {
            // Two concurrent first-time syncs for the same profile can both observe no snapshot and both
            // try to insert one; the loser's insert is rejected by the primary key. Detach the failed
            // tracked instance and re-apply this sync as an update against the winner's row instead of
            // surfacing a 500 (see ImportV1ProfileEndpoint: a V1 import's first request commonly races
            // exactly this path).
            db.Entry(snapshot).State = EntityState.Detached;
            snapshot = await db.PlayerDataSnapshots.FirstAsync(entity => entity.Id == profileId, ct);
            ApplyTransform(snapshot, transformed, isNew: false, syncedAt);
            await db.SaveChangesAsync(ct);
        }

        return new PlayerDataSyncResult.Success(snapshot);
    }

    private static void ApplyTransform(
        PlayerDataSnapshotEntity snapshot, PlayerDataTransformResult transformed, bool isNew, DateTimeOffset syncedAt)
    {
        var existingChunkHashes = snapshot.ChunkHashes;

        snapshot.ConfigHash = transformed.ConfigHash;
        snapshot.TacticusLastUpdatedOn = transformed.TacticusLastUpdatedOn;
        snapshot.SourceHash = transformed.SourceHash;
        snapshot.SchemaVersion = PlayerDataTransformer.CurrentSchemaVersion;
        snapshot.SyncedAt = syncedAt;
        snapshot.ChunkHashes = transformed.ChunkHashes;

        foreach (var (key, setter) in ChunkSetters)
        {
            var changed = isNew
                || existingChunkHashes.GetValueOrDefault(key, string.Empty) != transformed.ChunkHashes[key];

            if (changed)
            {
                setter(snapshot, transformed);
            }
        }
    }

    private static bool IsSnapshotPrimaryKeyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

public abstract record PlayerDataSyncResult
{
    public sealed record Success(PlayerDataSnapshotEntity Snapshot) : PlayerDataSyncResult;

    public sealed record UpstreamRejected : PlayerDataSyncResult;
}
