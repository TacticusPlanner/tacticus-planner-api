using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using OpenTelemetry;

namespace TacticusPlanner.ServiceDefaults;

/// <summary>
/// Trims spans before they are exported, so the telemetry shows what needs attention and ingestion stays cheap.
/// It must run before the exporter's processor: an exporter skips a span whose <see cref="Activity.Recorded"/>
/// flag is cleared, and Azure Monitor reads <see cref="Activity.Status"/> to decide whether a request failed.
/// <list type="bullet">
/// <item>Expected client errors (<see cref="ExpectedClientErrors"/>) on a request are marked <see cref="ActivityStatusCode.Ok"/>,
/// so they are not reported as failed requests. The status code is still exported.</item>
/// <item>Database spans that succeeded faster than <see cref="SlowDatabaseSpan"/> are dropped. Failed and slow ones are kept.</item>
/// <item>An outgoing span that failed by cancellation is dropped when the caller had already closed the request, since
/// the cancellation is the caller's doing. The request itself is still exported (as a 499).</item>
/// </list>
/// </summary>
public sealed class TelemetryNoiseProcessor(IHttpContextAccessor? httpContextAccessor) : BaseProcessor<Activity>
{
    /// <summary>The <c>ActivitySource</c> Npgsql traces commands and connection opens with.</summary>
    public const string DatabaseSourceName = "Npgsql";

    /// <summary>Database spans at least this long are kept; prod's p95 is under 10 ms.</summary>
    public static readonly TimeSpan SlowDatabaseSpan = TimeSpan.FromMilliseconds(100);

    /// <summary>Responses that are the designed outcome for a bad or stale client request, not a server failure:
    /// validation (400), auth (401), missing data (404), conflicts (409), the rate limiter (429) and requests the
    /// client abandoned (499).</summary>
    public static readonly IReadOnlySet<int> ExpectedClientErrors = new HashSet<int> { 400, 401, 404, 409, 429, 499 };

    public override void OnEnd(Activity data)
    {
        if (data.Kind == ActivityKind.Server)
        {
            if (data.Status == ActivityStatusCode.Unset
                && StatusCode(data) is { } statusCode
                && ExpectedClientErrors.Contains(statusCode))
            {
                data.SetStatus(ActivityStatusCode.Ok);
            }

            return;
        }

        if (data.Status == ActivityStatusCode.Error)
        {
            if (FailedFromCancellation(data)
                && httpContextAccessor?.HttpContext?.RequestAborted.IsCancellationRequested == true)
            {
                Drop(data);
            }

            return;
        }

        if (data.Source.Name == DatabaseSourceName && data.Duration < SlowDatabaseSpan)
        {
            Drop(data);
        }
    }

    /// <summary>Whether the span failed because it was cancelled, per its <c>error.type</c> tag or a recorded
    /// exception. Any other failure is kept even when the request was aborted.</summary>
    private static bool FailedFromCancellation(Activity activity) =>
        IsCancellation(activity.GetTagItem("error.type") as string)
        || activity.Events.Any(spanEvent => spanEvent.Name == "exception"
            && IsCancellation(spanEvent.Tags.FirstOrDefault(tag => tag.Key == "exception.type").Value as string));

    private static bool IsCancellation(string? type) =>
        type is not null && type.Contains("Cancel", StringComparison.OrdinalIgnoreCase);

    private static int? StatusCode(Activity activity) => activity.GetTagItem("http.response.status_code") switch
    {
        int value => value,
        string text when int.TryParse(text, out var parsed) => parsed,
        _ => null,
    };

    private static void Drop(Activity activity) => activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
}
