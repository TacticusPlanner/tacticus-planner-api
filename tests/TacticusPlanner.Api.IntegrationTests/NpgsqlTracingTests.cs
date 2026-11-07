using System.Collections.Concurrent;
using System.Diagnostics;
using Npgsql;

namespace TacticusPlanner.Api.IntegrationTests;

public sealed class NpgsqlTracingTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task DatabaseSpansCarryNoFirstResponseEvent()
    {
        var spans = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await GuildTestHelpers.CreateGuildReadyClientAsync(factory);

        // The listener is process-wide and test classes run in parallel: keep only this factory's database.
        var database = new NpgsqlConnectionStringBuilder(factory.ConnectionString).Database;
        var own = spans.Where(span => span.TagObjects.Any(tag => tag.Value as string == database)).ToList();
        Assert.NotEmpty(own);
        Assert.DoesNotContain(own, span => span.Events.Any(spanEvent => spanEvent.Name == "received-first-response"));
    }
}
