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
