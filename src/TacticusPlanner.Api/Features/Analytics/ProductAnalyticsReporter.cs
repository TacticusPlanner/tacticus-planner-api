namespace TacticusPlanner.Api.Features.Analytics;

/// <summary>
/// Reports a product event without letting a vendor-client failure change the outcome of the request
/// that triggered it. See design.md's "fire-and-forget capture" decision and
/// specs/product-analytics-events/spec.md's "Analytics never affects the operation that produced it" -
/// event reporting is not awaited on the request path, but an unhandled synchronous throw (e.g. a
/// full/closed batching channel) would still surface as a 500 for an otherwise-successful request
/// unless it is caught here, in the one place both emission call sites route through.
/// </summary>
public static partial class ProductAnalyticsReporter
{
    public static void TryReport(ILogger logger, string eventName, Action report)
    {
        try
        {
            report();
        }
        // codeql[cs/catch-of-all-exceptions]: deliberate - the whole point of this wrapper is that no
        // failure mode of a fire-and-forget analytics call, known or not, may turn a successful request
        // into a 500 (see the class doc comment / spec.md's "Analytics never affects the operation that
        // produced it"). Catching a narrower, enumerable set of exception types - the pattern used
        // elsewhere in this codebase for calls to a well-understood HTTP client - isn't available here
        // since the vendor SDK's failure modes aren't part of its documented contract.
        catch (Exception exception)
        {
            LogReportingFailed(logger, exception, eventName);
        }
    }

    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Warning,
        Message = "Failed to report the {EventName} product analytics event; the triggering request "
            + "completed normally regardless."
    )]
    private static partial void LogReportingFailed(ILogger logger, Exception exception, string eventName);
}
