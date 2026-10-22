using System.ComponentModel.DataAnnotations;
using System.Globalization;
using FastEndpoints;
using Microsoft.Extensions.Options;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;

namespace TacticusPlanner.Api.Features.UpstreamRateLimiting;

public sealed class UpstreamRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, int.MaxValue)]
    public int UpstreamFastPerMinute { get; set; } = 10;

    [Range(1, int.MaxValue)]
    public int UpstreamSlowPerHour { get; set; } = 6;
}

/// <summary>Endpoint metadata marking an endpoint that calls the Tacticus or V1 APIs on the caller's behalf.
/// <see cref="Fast"/> is <c>upstream-fast</c> (per minute); <see cref="Slow"/> is <c>upstream-slow</c> (per hour).</summary>
public sealed record UpstreamRateLimit(bool IsFast)
{
    public const string ResponseDescription = "The per-profile upstream-call limit for this endpoint is exhausted; see Retry-After.";

    public static readonly UpstreamRateLimit Fast = new(true);
    public static readonly UpstreamRateLimit Slow = new(false);
}

public static class UpstreamRateLimitingExtensions
{
    public static IServiceCollection AddUpstreamRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<UpstreamRateLimitOptions>()
            .Bind(configuration.GetSection(UpstreamRateLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<UpstreamRateLimiter>();

        return services;
    }
}

/// <summary>
/// Global pre-processor, registered right after <see cref="CurrentUserPreProcessor"/> so the bucket can be
/// keyed by profile id (falling back to <c>(issuer, subject)</c> before the profile is provisioned). A request
/// over the limit is rejected with 429 before any handler, upstream call, persistence or analytics runs.
/// </summary>
public sealed class UpstreamRateLimitPreProcessor : IGlobalPreProcessor
{
    public async Task PreProcessAsync(IPreProcessorContext context, CancellationToken ct)
    {
        var http = context.HttpContext;
        if (http.ResponseStarted()
            || http.GetEndpoint() is not RouteEndpoint endpoint
            || endpoint.Metadata.GetMetadata<UpstreamRateLimit>() is not { } policy)
        {
            return;
        }

        var user = http.ProcessorState<CurrentUserState>();
        if (user.Subject.Length == 0)
        {
            return;
        }

        var options = http.Resolve<IOptions<UpstreamRateLimitOptions>>().Value;
        var identity = user.ProfileId is { } profileId ? $"profile:{profileId}" : $"sub:{user.Issuer}|{user.Subject}";
        var retryAfter = http.Resolve<UpstreamRateLimiter>().TryAcquire(
            identity,
            http.Request.Method,
            endpoint.RoutePattern.RawText ?? endpoint.DisplayName ?? string.Empty,
            policy.IsFast ? options.UpstreamFastPerMinute : options.UpstreamSlowPerHour,
            policy.IsFast ? TimeSpan.FromMinutes(1) : TimeSpan.FromHours(1));
        if (retryAfter is not { } seconds)
        {
            return;
        }

        http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        await ApiProblems.WriteAsync(
            http, StatusCodes.Status429TooManyRequests, detail: $"Too many requests. Try again in {seconds} seconds.");
    }
}
