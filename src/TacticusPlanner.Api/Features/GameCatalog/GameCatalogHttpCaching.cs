using Microsoft.Net.Http.Headers;
using TacticusPlanner.GameCatalog;

namespace TacticusPlanner.Api.Features.GameCatalog;

internal static class GameCatalogHttpCaching
{
    public static string CreateEtag(string hash) => $"\"{hash}\"";

    public static string CreateFilteredEtag(
        string datasetHash,
        IEnumerable<KeyValuePair<string, string?>> query
    )
    {
        var normalizedQuery = new List<KeyValuePair<string, string>>();

        foreach (var (key, value) in query)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                normalizedQuery.Add(new KeyValuePair<string, string>(key, value.Trim().ToUpperInvariant()));
            }
        }

        return CreateEtag(GameCatalogHashing.ComputeQueryHash(datasetHash, normalizedQuery));
    }

    public static bool TryApplyNotModified(HttpContext httpContext, string etag)
    {
        if (!RequestMatchesEtag(httpContext, etag))
        {
            return false;
        }

        httpContext.Response.Headers.ETag = etag;
        httpContext.Response.StatusCode = StatusCodes.Status304NotModified;

        return true;
    }

    private static bool RequestMatchesEtag(HttpContext httpContext, string etag)
    {
        if (!httpContext.Request.Headers.TryGetValue(HeaderNames.IfNoneMatch, out var values))
        {
            return false;
        }

        return values
            .SelectMany(value => value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [])
            .Any(value => value == "*" || string.Equals(value, etag, StringComparison.Ordinal));
    }
}
