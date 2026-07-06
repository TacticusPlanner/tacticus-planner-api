using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.PlayerData;

/// <summary>
/// Returns the authenticated profile's current player-data manifest without contacting the Tacticus API —
/// a cheap staleness re-check the client can use before deciding whether to trigger a full sync.
/// </summary>
public sealed class GetPlayerDataManifestEndpoint : EndpointWithoutRequest<PlayerDataManifest>
{
    public override void Configure()
    {
        Get("me/player-data/manifest");
        Summary(summary =>
        {
            summary.Summary = "Gets the authenticated user's current player-data manifest.";
            summary.Description = "Returns the manifest from the last successful sync, without contacting "
                + "the Tacticus API. Use tacticus-integration/player-sync to actually refresh the data.";
            summary.Response<PlayerDataManifest>(StatusCodes.Status200OK, "The current player-data manifest.");
            summary.Response(StatusCodes.Status304NotModified, "The manifest has not changed.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No player data has been synced for this profile yet.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
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

        var etag = ETagHelper.CreateEtag(snapshot.SourceHash);
        if (ETagHelper.TryApplyNotModified(HttpContext, etag))
        {
            await Send.NotModifiedAsync(ct);
            return;
        }

        HttpContext.Response.Headers.ETag = etag;
        HttpContext.Response.Headers.CacheControl = "private, must-revalidate";
        HttpContext.Response.Headers.Vary = HeaderNames.Authorization;

        await Send.OkAsync(PlayerDataManifestBuilder.Build(snapshot), ct);
    }
}
