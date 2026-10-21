using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.IntegrationTests;

public sealed class CurrentUserLastSeenTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static bool WritesAccount(IEnumerable<string> commands) =>
        commands.Any(text => text.StartsWith("UPDATE accounts", StringComparison.Ordinal));

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

        Assert.True(WritesAccount(factory.CommandTextsFor(first)));
        Assert.False(WritesAccount(factory.CommandTextsFor(second)));
        Assert.False(WritesAccount(factory.CommandTextsFor(third)));
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
            await db.Accounts.IgnoreQueryFilters().Where(account => account.Subject == subject)
                .ExecuteUpdateAsync(set => set.SetProperty(account => account.LastSeenAt, stale), ct);
        }

        var refreshed = await client.GetAsync("/api/v1/me", ct);

        Assert.True(WritesAccount(factory.CommandTextsFor(refreshed)));
        Assert.True(await LastSeenAtAsync(subject) > stale.AddHours(1));
    }

    private async Task<DateTimeOffset?> LastSeenAtAsync(string subject)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        return await db.Accounts.IgnoreQueryFilters().Where(account => account.Subject == subject)
            .Select(account => account.LastSeenAt).SingleAsync(TestContext.Current.CancellationToken);
    }
}
