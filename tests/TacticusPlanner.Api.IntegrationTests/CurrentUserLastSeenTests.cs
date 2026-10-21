using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.IntegrationTests;

public sealed class CurrentUserLastSeenTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static bool WritesProfile(IEnumerable<string> commands) =>
        commands.Any(text => text.StartsWith("UPDATE profiles", StringComparison.Ordinal));

    [Fact]
    public async Task RepeatedMeCallsWithinAnHourWriteLastSeenOnlyOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        var subject = $"last-seen-{Guid.NewGuid()}";
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, subject);

        var first = await client.GetAsync("/api/v1/me", ct);
        var second = await client.GetAsync("/api/v1/me", ct);
        var third = await client.GetAsync("/api/v1/me", ct);

        Assert.True(WritesProfile(factory.CommandTextsFor(first)));
        Assert.False(WritesProfile(factory.CommandTextsFor(second)));
        Assert.False(WritesProfile(factory.CommandTextsFor(third)));
        Assert.Equal(await LastSeenAtAsync(subject), await LastSeenAtAsync(subject));
    }

    [Fact]
    public async Task StaleLastSeenIsRefreshed()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        var subject = $"last-seen-{Guid.NewGuid()}";
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, subject);
        (await client.GetAsync("/api/v1/me", ct)).EnsureSuccessStatusCode();
        var stale = DateTimeOffset.UtcNow.AddHours(-2);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
            await db.Profiles.IgnoreQueryFilters().Where(profile => profile.Subject == subject)
                .ExecuteUpdateAsync(set => set.SetProperty(profile => profile.LastSeenAt, stale), ct);
        }

        var refreshed = await client.GetAsync("/api/v1/me", ct);

        Assert.True(WritesProfile(factory.CommandTextsFor(refreshed)));
        Assert.True(await LastSeenAtAsync(subject) > stale.AddHours(1));
    }

    private async Task<DateTimeOffset?> LastSeenAtAsync(string subject)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        return await db.Profiles.IgnoreQueryFilters().Where(profile => profile.Subject == subject)
            .Select(profile => profile.LastSeenAt).SingleAsync(TestContext.Current.CancellationToken);
    }
}
