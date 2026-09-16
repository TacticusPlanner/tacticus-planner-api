using PostHog;
using PostHog.Config;

namespace TacticusPlanner.Api.Features.Analytics;

/// <summary>
/// The only file in the solution importing the PostHog SDK (see design.md's "internal capture
/// abstraction" decision) — every other call site depends on <see cref="IProductAnalytics"/> instead.
/// Calls are fire-and-forget: <see cref="IPostHogClient.Capture"/> enqueues synchronously and the SDK
/// batches/flushes on its own schedule, so reporting an event never awaits network I/O on the request
/// path and no per-request flush is added (design.md's "fire-and-forget capture" decision).
/// </summary>
internal sealed class PostHogProductAnalytics(IPostHogClient client) : IProductAnalytics
{
    public void AccountRegistered(string analyticsId) => Capture(analyticsId, "account_registered");

    public void GuildRegistered(string analyticsId) => Capture(analyticsId, "guild_registered");

    private void Capture(string analyticsId, string eventName) =>
        client.Capture(analyticsId, eventName, properties: null, groups: null, flags: null);
}

/// <summary>Registers the vendor SDK and the real adapter — kept beside <see cref="PostHogProductAnalytics"/>
/// so <see cref="DependencyInjection"/> never itself references the PostHog SDK.</summary>
internal static class PostHogRegistration
{
    public static IServiceCollection AddPostHogProductAnalytics(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPostHog(options =>
            options.UseConfigurationSection(configuration.GetSection(AnalyticsOptions.SectionName))
        );
        services.AddSingleton<IProductAnalytics, PostHogProductAnalytics>();
        return services;
    }
}
