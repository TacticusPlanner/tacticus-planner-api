using Microsoft.Net.Http.Headers;

namespace TacticusPlanner.Api.Http;

/// <summary>
/// Minimal strong-ETag conditional-request helper: wrap a content hash as an ETag and short-circuit a
/// matching <c>If-None-Match</c> request with <c>304 Not Modified</c>. Domain-agnostic — any endpoint with
/// a stable content hash can use it.
/// </summary>
internal static class ETagHelper
{
    public static string CreateEtag(string hash) => $"\"{hash}\"";

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
