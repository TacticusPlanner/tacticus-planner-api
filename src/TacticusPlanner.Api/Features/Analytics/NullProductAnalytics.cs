namespace TacticusPlanner.Api.Features.Analytics;

/// <summary>
/// Selected instead of the PostHog-backed adapter when <c>Analytics:ProjectToken</c> is absent — the
/// supported way to run with capture switched off (local development, automated tests, or a rollback that
/// clears the token). Every other behavior is unchanged; see specs/product-analytics-events/spec.md's
/// "Analytics is inert when not configured".
/// </summary>
internal sealed class NullProductAnalytics : IProductAnalytics
{
    public void AccountRegistered(string analyticsId)
    {
    }

    public void GuildRegistered(string analyticsId)
    {
    }
}
