using FastEndpoints;
using Microsoft.Net.Http.Headers;
using TacticusPlanner.GameCatalog;

namespace TacticusPlanner.Api.Features.GameCatalog;

public sealed class GetGameCatalogManifestEndpoint(IGameCatalogProvider catalog)
    : EndpointWithoutRequest<GameCatalogManifestResponse>
{
    public override void Configure()
    {
        Get("game-catalog/manifest");
        AllowAnonymous();
        Summary(summary =>
        {
            summary.Summary = "Gets the active game catalog manifest.";
            summary.Description = "Returns game catalog release metadata (version, schema version, game version), "
                + "source hash, and per-dataset hashes for the denormalized datasets.";
            summary.Response<GameCatalogManifestResponse>(
                StatusCodes.Status200OK,
                "The active game catalog manifest."
            );
            summary.Response(StatusCodes.Status304NotModified, "The manifest has not changed.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var snapshot = catalog.Current;
        var etag = GameCatalogHttpCaching.CreateEtag(snapshot.SourceHash);

        if (GameCatalogHttpCaching.TryApplyNotModified(HttpContext, etag))
        {
            await Send.NotModifiedAsync(ct);
            return;
        }

        HttpContext.Response.Headers.ETag = etag;
        HttpContext.Response.Headers.CacheControl = "private, must-revalidate";
        HttpContext.Response.Headers.Vary = HeaderNames.Authorization;

        var response = new GameCatalogManifestResponse(
            snapshot.Version,
            snapshot.SchemaVersion,
            snapshot.GameVersion,
            snapshot.SourceHash,
            snapshot.DatasetHashes
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new GameCatalogManifestDatasetResponse(pair.Key, pair.Value, GetDatasetUrl(pair.Key)))
                .ToArray()
        );

        await Send.OkAsync(response, ct);
    }

    // Served datasets are consolidated: each key maps 1:1 onto its route.
    private static string GetDatasetUrl(string datasetKey) => $"/api/v1/game-catalog/{datasetKey}";
}
