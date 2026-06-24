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
        Policies(AuthorizationPolicies.AccessAsUser);
        Summary(summary =>
        {
            summary.Summary = "Gets the active catalog manifest.";
            summary.Description = "Returns catalog release metadata, source hash, and per-dataset hashes.";
            summary.Response<CatalogManifestResponse>(
                StatusCodes.Status200OK,
                "The active catalog manifest."
            );
            summary.Response(StatusCodes.Status304NotModified, "The manifest has not changed.");
            summary.Response(StatusCodes.Status401Unauthorized, "Authentication is required.");
            summary.Response(StatusCodes.Status403Forbidden, "The authenticated user is not authorized.");
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
            snapshot.SourceHash,
            snapshot.DatasetHashes
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new CatalogManifestDatasetResponse(pair.Key, pair.Value, GetDatasetUrl(pair.Key)))
                .ToArray()
        );

        await Send.OkAsync(response, ct);
    }

    private static string GetDatasetUrl(string datasetKey) => datasetKey switch
    {
        CatalogDatasets.Units => "/api/v1/catalog/units",
        CatalogDatasets.Mows => "/api/v1/catalog/mows",
        CatalogDatasets.Upgrades => "/api/v1/catalog/upgrades",
        CatalogDatasets.Equipment => "/api/v1/catalog/equipment",
        CatalogDatasets.Campaigns => "/api/v1/catalog/campaigns",
        CatalogDatasets.CampaignEvents => "/api/v1/catalog/campaign-events",
        CatalogDatasets.CampaignBattles => "/api/v1/catalog/campaign-battles",
        CatalogDatasets.Lres => "/api/v1/catalog/lres",
        _ => throw new InvalidOperationException($"Unknown catalog dataset '{datasetKey}'."),
    };
}
