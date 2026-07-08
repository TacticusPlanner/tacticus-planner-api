using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Users;

namespace TacticusPlanner.Api.Features.PlayerData;

public sealed record GetPlayerDataChunkRequest(string Chunk);

/// <summary>
/// Serves one player-data chunk by key, mirroring the catalog's per-dataset endpoints but authenticated and
/// parameterized (one route for every chunk, rather than one endpoint class per chunk) since chunks are
/// per-profile rather than a small fixed set of public routes.
/// </summary>
public sealed class GetPlayerDataChunkEndpoint : Endpoint<GetPlayerDataChunkRequest, PlayerDataChunkEnvelope<object>>
{
    public override void Configure()
    {
        Get("me/player-data/{chunk}");
        Summary(summary =>
        {
            summary.Summary = "Gets one player-data chunk for the authenticated user.";
            summary.Description = "Chunk key must be one of the keys advertised by the player-data manifest.";
            summary.Response<PlayerDataChunkEnvelope<object>>(StatusCodes.Status200OK, "The requested chunk.");
            summary.Response(StatusCodes.Status304NotModified, "The chunk has not changed.");
            summary.Response(StatusCodes.Status400BadRequest, "Unknown chunk key.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No player data has been synced yet.");
        });
    }

    public override async Task HandleAsync(GetPlayerDataChunkRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();

        // Step 1: project only the hash/metadata columns (AsNoTracking) — never touch any of the ten
        // jsonb chunk payload columns yet. Keyed directly by the profile id already resolved by
        // CurrentUserPreProcessor, so this is a primary-key lookup rather than a Profile/Account join.
        var metadata = await db.PlayerDataSnapshots
            .AsNoTracking()
            .Where(entity => entity.Id == profileId)
            .Select(entity => new
            {
                entity.Id,
                entity.SchemaVersion,
                entity.ConfigHash,
                entity.SourceHash,
                entity.ChunkHashes,
            })
            .SingleOrDefaultAsync(ct);

        if (metadata is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var chunkHash = metadata.ChunkHashes.GetValueOrDefault(req.Chunk, string.Empty);
        var etag = ETagHelper.CreateEtag(chunkHash);
        if (ETagHelper.TryApplyNotModified(HttpContext, etag))
        {
            await Send.NotModifiedAsync(ct);
            return;
        }

        // Step 2: only reached when the chunk actually changed — fetch just the one requested jsonb
        // column via its own projected query, so the other nine never leave the database.
        var payload = await FetchChunkPayloadAsync(db, metadata.Id, req.Chunk, ct);

        HttpContext.Response.Headers.ETag = etag;
        HttpContext.Response.Headers.CacheControl = "private, must-revalidate";
        HttpContext.Response.Headers.Vary = HeaderNames.Authorization;

        var response = new PlayerDataChunkEnvelope<object>(
            metadata.SchemaVersion,
            metadata.ConfigHash,
            metadata.SourceHash,
            req.Chunk,
            chunkHash,
            payload!);

        await Send.OkAsync(response, ct);
    }

    private static Task<object?> FetchChunkPayloadAsync(
        PlannerDbContext db,
        ProfileId id,
        string chunk,
        CancellationToken ct)
    {
        var snapshots = db.PlayerDataSnapshots.AsNoTracking().Where(entity => entity.Id == id);

        return chunk switch
        {
            PlayerDataChunkKeys.PlayerDetails =>
                snapshots.Select(entity => (object?)entity.PlayerDetails).SingleOrDefaultAsync(ct),
            PlayerDataChunkKeys.Characters =>
                snapshots.Select(entity => (object?)entity.Characters).SingleOrDefaultAsync(ct),
            PlayerDataChunkKeys.Mows =>
                snapshots.Select(entity => (object?)entity.Mows).SingleOrDefaultAsync(ct),
            PlayerDataChunkKeys.InventoryUpgrades =>
                snapshots.Select(entity => (object?)entity.InventoryUpgrades).SingleOrDefaultAsync(ct),
            PlayerDataChunkKeys.InventoryItems =>
                snapshots.Select(entity => (object?)entity.InventoryItems).SingleOrDefaultAsync(ct),
            PlayerDataChunkKeys.Inventory =>
                snapshots.Select(entity => (object?)entity.Inventory).SingleOrDefaultAsync(ct),
            PlayerDataChunkKeys.CampaignProgress =>
                snapshots.Select(entity => (object?)entity.CampaignProgress).SingleOrDefaultAsync(ct),
            PlayerDataChunkKeys.CampaignEventsProgress =>
                snapshots.Select(entity => (object?)entity.CampaignEventsProgress).SingleOrDefaultAsync(ct),
            PlayerDataChunkKeys.LiveProgress =>
                snapshots.Select(entity => (object?)entity.LiveProgress).SingleOrDefaultAsync(ct),
            PlayerDataChunkKeys.LreProgress =>
                snapshots.Select(entity => (object?)entity.LreProgress).SingleOrDefaultAsync(ct),
            _ => throw new ArgumentOutOfRangeException(nameof(chunk), chunk, "Unknown player-data chunk key."),
        };
    }
}
