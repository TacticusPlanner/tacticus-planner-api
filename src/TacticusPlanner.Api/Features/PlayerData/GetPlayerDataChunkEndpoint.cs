using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Persistence;
using PlayerDataSnapshotEntity = TacticusPlanner.Persistence.Users.PlayerData.PlayerDataSnapshot;

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
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "Unknown chunk key, or no player data has been synced yet.");
        });
    }

    public override async Task HandleAsync(GetPlayerDataChunkRequest req, CancellationToken ct)
    {
        if (!PlayerDataChunkKeys.All.Contains(req.Chunk, StringComparer.Ordinal))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var issuer = User.FindFirstValue("iss");
        var subject = User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var snapshot = await db.PlayerDataSnapshots
            .Include(entity => entity.Profile)
            .ThenInclude(entity => entity!.Account)
            .SingleOrDefaultAsync(
                entity => entity.Profile!.Account!.Issuer == issuer && entity.Profile.Account.Subject == subject,
                ct);

        if (snapshot is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var chunkHash = snapshot.ChunkHashes.GetValueOrDefault(req.Chunk, string.Empty);
        var etag = ETagHelper.CreateEtag(chunkHash);
        if (ETagHelper.TryApplyNotModified(HttpContext, etag))
        {
            await Send.NotModifiedAsync(ct);
            return;
        }

        HttpContext.Response.Headers.ETag = etag;
        HttpContext.Response.Headers.CacheControl = "private, must-revalidate";
        HttpContext.Response.Headers.Vary = HeaderNames.Authorization;

        var payload = SelectPayload(snapshot, req.Chunk);
        var response = new PlayerDataChunkEnvelope<object>(
            snapshot.SchemaVersion,
            snapshot.ConfigHash,
            snapshot.SourceHash,
            req.Chunk,
            chunkHash,
            payload);

        await Send.OkAsync(response, ct);
    }

    private static object SelectPayload(PlayerDataSnapshotEntity snapshot, string chunk) => chunk switch
    {
        PlayerDataChunkKeys.PlayerDetails => snapshot.PlayerDetails,
        PlayerDataChunkKeys.Characters => snapshot.Characters,
        PlayerDataChunkKeys.Mows => snapshot.Mows,
        PlayerDataChunkKeys.InventoryUpgrades => snapshot.InventoryUpgrades,
        PlayerDataChunkKeys.InventoryItems => snapshot.InventoryItems,
        PlayerDataChunkKeys.Inventory => snapshot.Inventory,
        PlayerDataChunkKeys.CampaignProgress => snapshot.CampaignProgress,
        PlayerDataChunkKeys.CampaignEventsProgress => snapshot.CampaignEventsProgress,
        PlayerDataChunkKeys.GameModeTokens => snapshot.GameModeTokens,
        PlayerDataChunkKeys.LreProgress => snapshot.LreProgress,
        _ => throw new ArgumentOutOfRangeException(nameof(chunk), chunk, "Unknown player-data chunk key."),
    };
}
