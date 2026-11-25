using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using OpenTelemetry;
using OpenTelemetry.Trace;
using TacticusPlanner.ServiceDefaults;

namespace TacticusPlanner.Api.Tests;

/// <summary>Runs spans through a real tracer pipeline (processor, then an export processor) to check what
/// would reach Azure Monitor and with which status.</summary>
public sealed class TelemetryNoiseProcessorTests
{
    private const string ServerSourceName = "TelemetryNoiseProcessorTests.Server";
    private const string ClientSourceName = "TelemetryNoiseProcessorTests.Client";

    private sealed class CollectingExporter(List<Activity> exported) : BaseExporter<Activity>
    {
        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                exported.Add(activity);
            }

            return ExportResult.Success;
        }
    }

    private static readonly ActivitySource Server = new(ServerSourceName);
    private static readonly ActivitySource Client = new(ClientSourceName);
    private static readonly ActivitySource Database = new(TelemetryNoiseProcessor.DatabaseSourceName);

    private static List<Activity> Run(Action emit, IHttpContextAccessor? accessor = null)
    {
        var exported = new List<Activity>();
        using (Sdk.CreateTracerProviderBuilder()
            .AddSource(ServerSourceName, ClientSourceName, TelemetryNoiseProcessor.DatabaseSourceName)
            .SetSampler(new AlwaysOnSampler())
            .AddProcessor(new TelemetryNoiseProcessor(accessor))
            .AddProcessor(new SimpleActivityExportProcessor(new CollectingExporter(exported)))
            .Build())
        {
            emit();
        }

        return exported;
    }

    private static void Emit(ActivitySource source, ActivityKind kind, TimeSpan duration, Action<Activity>? configure = null)
    {
        using var activity = source.StartActivity("span", kind)!;
        // Both ends are set so the duration is exactly what was asked for, however long the thread is paused.
        var start = DateTime.UtcNow - duration;
        activity.SetStartTime(start);
        activity.SetEndTime(start + duration);
        configure?.Invoke(activity);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(429)]
    [InlineData(499)]
    public void ExpectedClientErrorsAreExportedAsSuccessfulRequests(int statusCode)
    {
        var exported = Run(() => Emit(Server, ActivityKind.Server, TimeSpan.FromMilliseconds(5),
            activity => activity.SetTag("http.response.status_code", statusCode)));

        Assert.Equal(ActivityStatusCode.Ok, Assert.Single(exported).Status);
    }

    [Theory]
    [InlineData(200, ActivityStatusCode.Unset)]
    [InlineData(403, ActivityStatusCode.Unset)]
    [InlineData(500, ActivityStatusCode.Error)]
    public void OtherRequestOutcomesAreLeftAsTheyAre(int statusCode, ActivityStatusCode status)
    {
        var exported = Run(() => Emit(Server, ActivityKind.Server, TimeSpan.FromMilliseconds(5), activity =>
        {
            activity.SetTag("http.response.status_code", statusCode);
            activity.SetStatus(status);
        }));

        Assert.Equal(status, Assert.Single(exported).Status);
    }

    [Fact]
    public void FastSuccessfulDatabaseSpansAreDroppedAndSlowOrFailedOnesKept()
    {
        var exported = Run(() =>
        {
            Emit(Database, ActivityKind.Client, TimeSpan.FromMilliseconds(5));
            Emit(Database, ActivityKind.Client, TimeSpan.FromMilliseconds(250), activity => activity.DisplayName = "slow");
            Emit(Database, ActivityKind.Client, TimeSpan.FromMilliseconds(5), activity =>
            {
                activity.DisplayName = "failed";
                activity.SetStatus(ActivityStatusCode.Error);
            });
        });

        Assert.Equal(["slow", "failed"], exported.Select(activity => activity.DisplayName));
    }

    [Fact]
    public void FastOutgoingHttpSpansAreKept()
    {
        var exported = Run(() => Emit(Client, ActivityKind.Client, TimeSpan.FromMilliseconds(5)));

        Assert.Single(exported);
    }

    [Fact]
    public void CancelledOutgoingSpansAreDroppedOnlyWhenTheCallerClosedTheRequest()
    {
        using var aborted = new CancellationTokenSource();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { RequestAborted = aborted.Token } };
        void EmitCancelled() => Emit(Client, ActivityKind.Client, TimeSpan.FromSeconds(10), activity =>
        {
            activity.SetTag("error.type", typeof(TaskCanceledException).FullName);
            activity.SetStatus(ActivityStatusCode.Error);
        });

        Assert.Single(Run(EmitCancelled, accessor));

        aborted.Cancel();
        Assert.Empty(Run(EmitCancelled, accessor));
    }

    [Fact]
    public void OtherFailuresAreKeptWhenTheCallerClosedTheRequest()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { RequestAborted = aborted.Token } };

        var exported = Run(() =>
        {
            Emit(Database, ActivityKind.Client, TimeSpan.FromMilliseconds(5), activity =>
            {
                activity.DisplayName = "failed";
                activity.AddException(new InvalidOperationException("23505"));
                activity.SetStatus(ActivityStatusCode.Error);
            });
            Emit(Database, ActivityKind.Client, TimeSpan.FromMilliseconds(5), activity =>
            {
                activity.DisplayName = "cancelled";
                activity.AddException(new OperationCanceledException());
                activity.SetStatus(ActivityStatusCode.Error);
            });
        }, accessor);

        Assert.Equal("failed", Assert.Single(exported).DisplayName);
    }
}
