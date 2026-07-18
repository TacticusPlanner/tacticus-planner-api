using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Refit;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Persistence;
using TacticusPlanner.TacticusApi;
using PlayerDataSnapshotEntity = TacticusPlanner.Domain.PlayerData.PlayerDataSnapshot;

namespace TacticusPlanner.Api.Features.PlayerData;

/// <summary>
/// Syncs the authenticated profile's player data from the Tacticus API: fetches the player endpoint,
/// transforms and normalizes the response (never storing it raw — ADR 0007), and persists it as a
/// <see cref="PlayerDataSnapshotEntity"/> unless the incoming <c>configHash</c> matches what is already
/// stored. A successful unchanged sync still advances the snapshot's sync timestamp.
/// </summary>
public sealed class PlayerSyncEndpoint(ITacticusApi tacticusApi, PlayerDataTransformer transformer)
    : EndpointWithoutRequest<PlayerDataManifest>
{
    public override void Configure()
    {
        Post("tacticus-integration/player-sync");
        Summary(summary =>
        {
            summary.Summary = "Syncs the authenticated user's player data from the Tacticus API.";
            summary.Description = "Fetches the current player from the Tacticus API, transforms it into "
                + "normalized chunks, and persists it. If the incoming configHash matches the last synced "
                + "value, chunk persistence is skipped but the snapshot sync timestamp is advanced.";
            summary.Response<PlayerDataManifest>(StatusCodes.Status200OK, "The current player-data manifest.");
            summary.Response(StatusCodes.Status400BadRequest, "No Tacticus API key is configured, or the Tacticus API rejected it.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated user has not signed up yet.");
        });
    }

    /// <summary>One setter per served chunk, applied only when that chunk's content hash actually changed
    /// (see <see cref="HandleAsync"/>) — keeps EF from marking every owned-json column dirty on every sync,
    /// so <c>SaveChangesAsync</c> only writes the columns that changed.</summary>
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

    public override async Task HandleAsync(CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var goalAchievementEvaluator = Resolve<GoalAchievementEvaluator>();

        // TacticusIntegration is keyed 1:1 by the same ProfileId, so this is a direct primary-key lookup —
        // no Account/Profile join, and the (potentially large) PlayerDataSnapshot row isn't touched yet.
        var integration = await db.TacticusIntegrations.FirstOrDefaultAsync(entity => entity.Id == profileId, ct);
        if (integration is null || integration.TacticusApiKey is null)
        {
            AddError("No Tacticus API key is configured for this profile.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var apiKey = integration.TacticusApiKey;

        var timeProvider = Resolve<TimeProvider>();
        integration.TacticusSyncLastAttemptedAt = timeProvider.GetUtcNow();

        TacticusApi.Models.Player.PlayerResponse response;
        try
        {
            response = await tacticusApi.GetPlayerAsync(apiKey, ct);
        }
        catch (ApiException exception) when ((int)exception.StatusCode is 400 or 401 or 403 or 404)
        {
            await db.SaveChangesAsync(ct);
            AddError("The Tacticus API could not fetch player data for the configured key.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var incomingConfigHash = response.Metadata?.ConfigHash ?? string.Empty;

        // Step 1: compare against just the hash/metadata columns (AsNoTracking) — none of the jsonb
        // chunk payload columns are read unless the game configuration actually changed.
        var existingMetadata = await db.PlayerDataSnapshots
            .AsNoTracking()
            .Where(entity => entity.Id == profileId)
            .Select(entity => new
            {
                entity.SchemaVersion,
                entity.ConfigHash,
                entity.SourceHash,
                entity.SyncedAt,
                entity.ChunkHashes,
            })
            .FirstOrDefaultAsync(ct);

        var canReuseExistingSnapshot = existingMetadata is not null
            && existingMetadata.ConfigHash == incomingConfigHash
            && existingMetadata.SchemaVersion == PlayerDataTransformer.CurrentSchemaVersion
            && PlayerDataChunkKeys.All.All(key =>
                existingMetadata.ChunkHashes.TryGetValue(key, out var hash) && !string.IsNullOrEmpty(hash));

        if (canReuseExistingSnapshot && existingMetadata is not null)
        {
            // The game configuration is unchanged, but this was still a successful upstream sync.
            // Advance the user-visible timestamp while retaining the existing normalized chunks.
            var syncedAt = timeProvider.GetUtcNow();
            integration.TacticusSyncLastSucceededAt = syncedAt;
            var currentSnapshot = await db.PlayerDataSnapshots.FirstAsync(entity => entity.Id == profileId, ct);
            currentSnapshot.SyncedAt = syncedAt;
            await goalAchievementEvaluator.EvaluateAsync(profileId, currentSnapshot, ct);
            await db.SaveChangesAsync(ct);
            await Send.OkAsync(PlayerDataManifestBuilder.Build(currentSnapshot), ct);
            return;
        }

        var transformed = transformer.Transform(response);

        // Step 2: only reached when the game configuration changed — load (or create) the tracked
        // snapshot, then assign only the chunk properties whose content hash differs from what's already
        // stored, so SaveChangesAsync marks just those owned-json columns dirty instead of all ten.
        var snapshot = await db.PlayerDataSnapshots.FirstOrDefaultAsync(entity => entity.Id == profileId, ct);
        var isNew = snapshot is null;
        snapshot ??= new PlayerDataSnapshotEntity { Id = profileId };

        snapshot.ConfigHash = transformed.ConfigHash;
        snapshot.TacticusLastUpdatedOn = transformed.TacticusLastUpdatedOn;
        snapshot.SourceHash = transformed.SourceHash;
        snapshot.SchemaVersion = PlayerDataTransformer.CurrentSchemaVersion;
        snapshot.SyncedAt = timeProvider.GetUtcNow();
        snapshot.ChunkHashes = transformed.ChunkHashes;

        foreach (var (key, setter) in ChunkSetters)
        {
            var changed = isNew
                || existingMetadata!.ChunkHashes.GetValueOrDefault(key, string.Empty) != transformed.ChunkHashes[key];

            if (changed)
            {
                setter(snapshot, transformed);
            }
        }

        if (isNew)
        {
            db.PlayerDataSnapshots.Add(snapshot);
        }

        integration.TacticusSyncLastSucceededAt = timeProvider.GetUtcNow();
        await goalAchievementEvaluator.EvaluateAsync(profileId, snapshot, ct);
        await db.SaveChangesAsync(ct);

        await Send.OkAsync(PlayerDataManifestBuilder.Build(snapshot), ct);
    }
}
