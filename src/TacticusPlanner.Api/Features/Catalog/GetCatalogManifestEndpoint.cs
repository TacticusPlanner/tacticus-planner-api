using FastEndpoints;
using Microsoft.Net.Http.Headers;
using TacticusPlanner.Catalog;

namespace TacticusPlanner.Api.Features.Catalog;

public sealed class GetCatalogManifestEndpoint(ICatalogProvider catalog)
    : EndpointWithoutRequest<CatalogManifestResponse>
{
    public override void Configure()
    {
        Get("catalog/manifest");
        AllowAnonymous();
        Summary(summary =>
        {
            summary.Summary = "Gets the active catalog manifest.";
            summary.Description = "Returns catalog release metadata (version, schema version, game version), "
                + "source hash, and per-dataset hashes for the denormalized datasets.";
            summary.Response<CatalogManifestResponse>(
                StatusCodes.Status200OK,
                "The active catalog manifest."
            );
            summary.Response(StatusCodes.Status304NotModified, "The manifest has not changed.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var snapshot = catalog.Current;
        var etag = CatalogHttpCaching.CreateEtag(snapshot.SourceHash);

        if (CatalogHttpCaching.TryApplyNotModified(HttpContext, etag))
        {
            await Send.NotModifiedAsync(ct);
            return;
        }

        HttpContext.Response.Headers.ETag = etag;
        HttpContext.Response.Headers.CacheControl = "private, must-revalidate";
        HttpContext.Response.Headers.Vary = HeaderNames.Authorization;

        var response = new CatalogManifestResponse(
            snapshot.Version,
            snapshot.SchemaVersion,
            snapshot.GameVersion,
            snapshot.SourceHash,
            snapshot.DatasetHashes
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new CatalogManifestDatasetResponse(pair.Key, pair.Value, GetDatasetUrl(pair.Key)))
                .ToArray()
        );

        await Send.OkAsync(response, ct);
    }

    // Served datasets are consolidated: each key maps 1:1 onto its route.
    private static string GetDatasetUrl(string datasetKey) => $"/api/v1/catalog/{datasetKey}";
}
