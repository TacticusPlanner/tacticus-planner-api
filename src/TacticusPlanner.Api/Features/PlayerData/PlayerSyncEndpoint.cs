using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Refit;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Persistence;
using TacticusPlanner.TacticusApi;
using PlayerDataSnapshotEntity = TacticusPlanner.Domain.PlayerData.PlayerDataSnapshot;

namespace TacticusPlanner.Api.Features.PlayerData;

/// <summary>
/// Syncs the authenticated profile's player data from the Tacticus API: fetches the player endpoint,
/// transforms and normalizes the response (never storing it raw — ADR 0007), and persists it as a
/// <see cref="PlayerDataSnapshotEntity"/>. Every successful response is transformed and canonically
/// hashed; only chunks whose transformed content changed are replaced. A successful unchanged sync
/// still advances the snapshot's sync timestamp.
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
                + "normalized chunks, and persists chunks whose canonical content hash changed. The game "
                + "configuration hash is retained as metadata and does not suppress player-content updates.";
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

        var transformed = transformer.Transform(response);

        // Always evaluate transformed player content. configHash identifies the game configuration that
        // contextualized the response; it is not a player-content version. Per-chunk canonical hashes
        // remain the write optimization, so unchanged owned-json columns are not replaced.
        var snapshot = await db.PlayerDataSnapshots.FirstOrDefaultAsync(entity => entity.Id == profileId, ct);
        var isNew = snapshot is null;
        snapshot ??= new PlayerDataSnapshotEntity { Id = profileId };
        var existingChunkHashes = snapshot.ChunkHashes;
        var syncedAt = timeProvider.GetUtcNow();

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

        if (isNew)
        {
            db.PlayerDataSnapshots.Add(snapshot);
        }

        integration.TacticusSyncLastSucceededAt = syncedAt;
        await db.SaveChangesAsync(ct);

        await Send.OkAsync(PlayerDataManifestBuilder.Build(snapshot), ct);
    }
}
