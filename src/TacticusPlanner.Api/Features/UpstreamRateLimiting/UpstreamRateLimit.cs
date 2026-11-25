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
    public int UpstreamSlowPerHour { get; set; } = 10;

    /// <summary>Player-data sync's own hourly limit. Auto-sync runs hourly, so this only bounds manual "Sync now",
    /// which players press after each in-game session; 10 an hour turned away a few of them every day.</summary>
    [Range(1, int.MaxValue)]
    public int PlayerDataSyncPerHour { get; set; } = 30;
}

/// <summary>Which configured limit an <see cref="UpstreamRateLimit"/> applies.</summary>
public enum UpstreamRateLimitBucket
{
    /// <summary><c>upstream-fast</c>: <see cref="UpstreamRateLimitOptions.UpstreamFastPerMinute"/> per minute.</summary>
    Fast,

    /// <summary><c>upstream-slow</c>: <see cref="UpstreamRateLimitOptions.UpstreamSlowPerHour"/> per hour.</summary>
    Slow,

    /// <summary><c>player-sync</c>: <see cref="UpstreamRateLimitOptions.PlayerDataSyncPerHour"/> per hour.</summary>
    PlayerSync,
}

/// <summary>Endpoint metadata marking an endpoint that calls the Tacticus or V1 APIs on the caller's behalf.
/// <see cref="Fast"/> is <c>upstream-fast</c> (per minute); <see cref="Slow"/> is <c>upstream-slow</c> (per hour);
/// <see cref="PlayerSync"/> is player-data sync's own hourly limit.
/// <see cref="Exempt"/>, when set, lets a request through unlimited when it makes no upstream call (decided from the
/// route and query only: the body is not read yet).</summary>
public sealed record UpstreamRateLimit(UpstreamRateLimitBucket Bucket, Func<HttpRequest, bool>? Exempt = null)
{
    public const string ResponseDescription = "The per-profile upstream-call limit for this endpoint is exhausted; see Retry-After.";

    public static readonly UpstreamRateLimit Fast = new(UpstreamRateLimitBucket.Fast);
    public static readonly UpstreamRateLimit Slow = new(UpstreamRateLimitBucket.Slow);
    public static readonly UpstreamRateLimit PlayerSync = new(UpstreamRateLimitBucket.PlayerSync);

    /// <summary><c>upstream-slow</c>, except for requests <paramref name="exempt"/> accepts.</summary>
    public static UpstreamRateLimit SlowUnless(Func<HttpRequest, bool> exempt) => new(UpstreamRateLimitBucket.Slow, exempt);
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
            || endpoint.Metadata.GetMetadata<UpstreamRateLimit>() is not { } policy
            || policy.Exempt?.Invoke(http.Request) == true)
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
        var (permits, window) = policy.Bucket switch
        {
            UpstreamRateLimitBucket.Fast => (options.UpstreamFastPerMinute, TimeSpan.FromMinutes(1)),
            UpstreamRateLimitBucket.PlayerSync => (options.PlayerDataSyncPerHour, TimeSpan.FromHours(1)),
            _ => (options.UpstreamSlowPerHour, TimeSpan.FromHours(1)),
        };
        var retryAfter = http.Resolve<UpstreamRateLimiter>().TryAcquire(
            identity,
            http.Request.Method,
            endpoint.RoutePattern.RawText ?? endpoint.DisplayName ?? string.Empty,
            permits,
            window);
        if (retryAfter is not { } seconds)
        {
            return;
        }

        http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        await ApiProblems.WriteAsync(
            http, StatusCodes.Status429TooManyRequests, detail: $"Too many requests. Try again in {seconds} seconds.");
    }
}
