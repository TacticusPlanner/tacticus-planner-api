using Microsoft.Net.Http.Headers;
using TacticusPlanner.Catalog;

namespace TacticusPlanner.Api.Features.Catalog;

internal static class CatalogEndpointSender
{
    public static CatalogItemsResponse<TItem>? CreateResponse<TItem>(
        HttpContext httpContext,
        CatalogSnapshot snapshot,
        string dataset,
        IEnumerable<TItem> items,
        CatalogQuery query,
        IEnumerable<KeyValuePair<string, string?>>? etagComponents = null
    )
    {
        var datasetHash = snapshot.DatasetHashes[dataset];
        var normalizedEtagComponents = etagComponents is null
            ? query.NormalizedFilters
            : query.NormalizedFilters.Concat(etagComponents);
        var hasEtagComponents = query.HasFilters || etagComponents is not null;
        var etag = hasEtagComponents
            ? CatalogHttpCaching.CreateFilteredEtag(datasetHash, normalizedEtagComponents)
            : CatalogHttpCaching.CreateEtag(datasetHash);

        if (CatalogHttpCaching.TryApplyNotModified(httpContext, etag))
        {
            return null;
        }

        httpContext.Response.Headers.ETag = etag;
        httpContext.Response.Headers.CacheControl = "private, must-revalidate";
        httpContext.Response.Headers.Vary = HeaderNames.Authorization;

        return new CatalogItemsResponse<TItem>(
            snapshot.Version,
            snapshot.SourceHash,
            datasetHash,
            items.ToArray()
        );
    }
}
