using FastEndpoints;
using Microsoft.Net.Http.Headers;
using TacticusPlanner.Api.Http;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Features.GameCatalog;

public sealed class GetGameCatalogManifestEndpoint(IGameCatalogProvider catalog)
    : EndpointWithoutRequest<GameCatalogManifest>
{
    public override void Configure()
    {
        Get("game-catalog/manifest");
        AllowAnonymous();
        Summary(summary =>
        {
            summary.Summary = "Gets the active game catalog manifest.";
            summary.Description = "Returns game catalog release metadata (version, schema version, game version), "
                + "source hash, and the denormalized dataset hashes + download urls.";
            summary.Response<GameCatalogManifest>(
                StatusCodes.Status200OK,
                "The active game catalog manifest."
            );
            summary.Response(StatusCodes.Status304NotModified, "The manifest has not changed.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var snapshot = catalog.Current;
        var etag = ETagHelper.CreateEtag(snapshot.SourceHash);

        if (ETagHelper.TryApplyNotModified(HttpContext, etag))
        {
            await Send.NotModifiedAsync(ct);
            return;
        }

        HttpContext.Response.Headers.ETag = etag;
        HttpContext.Response.Headers.CacheControl = "private, must-revalidate";
        HttpContext.Response.Headers.Vary = HeaderNames.Authorization;

        await Send.OkAsync(snapshot.Manifest, ct);
    }
}
