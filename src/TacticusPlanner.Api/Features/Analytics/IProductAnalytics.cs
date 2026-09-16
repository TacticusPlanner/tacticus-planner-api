namespace TacticusPlanner.Api.Features.Analytics;

/// <summary>
/// The only way any feature slice reports a product event. One typed method per declared event — no
/// free-form property-bag overload — so the privacy floor in specs/product-analytics-events/spec.md
/// (no account id, display name, email, API key, or free text ever leaves the process) is a compile-time
/// property of the call sites rather than a review convention. See design.md's "typed event methods"
/// decision.
/// </summary>
public interface IProductAnalytics
{
    /// <summary>An account was provisioned for a caller who did not previously have one.</summary>
    void AccountRegistered(string analyticsId);

    /// <summary>A guild was registered for a profile for the first time (not a re-registration).</summary>
    void GuildRegistered(string analyticsId);
}
