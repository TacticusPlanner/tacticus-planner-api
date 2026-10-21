using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.TacticusIntegration;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.IntegrationTests;

public sealed class UpstreamRateLimitEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string Sync = "/api/v1/guilds/me/sync";
    private const string PlayerSync = "/api/v1/tacticus-integration/player-sync";
    private const string Validate = "/api/v1/tacticus-api-key/validate";

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path) =>
        client.PostAsync(path, null, TestContext.Current.CancellationToken);

    private static async Task<List<HttpStatusCode>> RepeatAsync(HttpClient client, string path, int count)
    {
        var statuses = new List<HttpStatusCode>();
        for (var index = 0; index < count; index++)
        {
            statuses.Add((await PostAsync(client, path)).StatusCode);
        }

        return statuses;
    }

    [Fact]
    public async Task SlowPolicyAllowsSixPerHourThenRejectsWithRetryAfterAndIsolatesEndpointsAndProfiles()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, await RepeatAsync(client, Sync, 6));
        var rejected = await PostAsync(client, Sync);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var retryAfter = Assert.IsType<TimeSpan>(rejected.Headers.RetryAfter?.Delta);
        Assert.InRange(retryAfter.TotalSeconds, 1, 3600);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await PostAsync(client, PlayerSync)).StatusCode);

        var other = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await PostAsync(other, Sync)).StatusCode);
    }

    [Fact]
    public async Task FastPolicyAllowsTenPerMinuteThenRejects()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var body = new ValidateTacticusApiKeyRequest(FakeTacticusApi.ValidKey);

        for (var index = 0; index < 10; index++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Validate, body, ct)).StatusCode);
        }

        var rejected = await client.PostAsJsonAsync(Validate, body, ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.InRange(rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);
    }

    [Fact]
    public async Task OtherEndpointsAreNeverLimited()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        for (var index = 0; index < 100; index++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me/goals", TestContext.Current.CancellationToken)).StatusCode);
        }
    }

    [Fact]
    public async Task RejectedSyncDoesNotTouchTheLastAttemptedTimestamp()
    {
        var ct = TestContext.Current.CancellationToken;
        var subject = $"rate-limit-{Guid.NewGuid()}";
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory, subject);
        (await client.PutAsJsonAsync(
            "/api/v1/me/tacticus-integration", new UpdateTacticusIntegrationRequest(FakeTacticusApi.ValidKey, null), ct))
            .EnsureSuccessStatusCode();
        for (var index = 0; index < 6; index++)
        {
            (await PostAsync(client, PlayerSync)).EnsureSuccessStatusCode();
        }

        var before = await LastAttemptedAtAsync(subject);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostAsync(client, PlayerSync)).StatusCode);

        Assert.NotNull(before);
        Assert.Equal(before, await LastAttemptedAtAsync(subject));
    }

    [Fact]
    public async Task ConfiguredLimitAppliesBeforeTheProfileIsProvisioned()
    {
        using var limited = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:UpstreamSlowPerHour", "1"));
        var client = limited.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, $"unprovisioned-{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(client, Sync)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostAsync(client, Sync)).StatusCode);
    }

    private async Task<DateTimeOffset?> LastAttemptedAtAsync(string subject)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var snapshotId = await db.PlayerDataSnapshots.IgnoreQueryFilters()
            .Where(entity => entity.Profile!.Account!.Subject == subject)
            .Select(entity => entity.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
        return await db.TacticusIntegrations.IgnoreQueryFilters()
            .Where(entity => entity.Id == snapshotId)
            .Select(entity => entity.TacticusSyncLastAttemptedAt)
            .SingleAsync(TestContext.Current.CancellationToken);
    }
}
