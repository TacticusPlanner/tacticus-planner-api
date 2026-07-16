using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.PlayerDataOverrides;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Tests;

public sealed class OnslaughtProgressEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task GetReturnsV1CompatibleDefaultsAndPutPersistsAllAlliances()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var initial = await client.GetFromJsonAsync<OnslaughtProgressResponse>(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            TestContext.Current.CancellationToken);

        Assert.NotNull(initial);
        Assert.Equal(new("Stone", 1), initial.Imperial);
        Assert.Equal(new("Stone", 1), initial.Xenos);
        Assert.Equal(new("Stone", 1), initial.Chaos);

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            new UpdateOnslaughtProgressRequest(
                new("Gold", 2),
                new("Diamond", 3),
                new("Silver", 1),
                initial.Revision),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<OnslaughtProgressResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(updated);
        Assert.Equal(new("Gold", 2), updated.Imperial);
        Assert.Equal(new("Diamond", 3), updated.Xenos);
        Assert.Equal(new("Silver", 1), updated.Chaos);
        Assert.True(updated.Revision > initial.Revision);
    }

    [Fact]
    public async Task PutPreservesOtherPlayerDataOverrides()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var initial = await client.GetFromJsonAsync<OnslaughtProgressResponse>(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            TestContext.Current.CancellationToken);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
            var row = db.PlayerDataOverrides.OrderByDescending(item => item.CreatedAt).First();
            row.CampaignProgressOverrides.Add(new() { HighestCompletedNodeNumber = 42 });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var current = await client.GetFromJsonAsync<OnslaughtProgressResponse>(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            TestContext.Current.CancellationToken);
        var response = await client.PutAsJsonAsync(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            new UpdateOnslaughtProgressRequest(new("Iron", 1), new("Iron", 1), new("Iron", 1), current!.Revision),
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        Assert.Contains(
            verificationDb.PlayerDataOverrides,
            item => item.CampaignProgressOverrides.Count == 1 && item.OnslaughtProgressOverrides.Count == 3);
    }

    [Theory]
    [InlineData("Wood", 1)]
    [InlineData("Stone", 0)]
    [InlineData("Stone", 4)]
    public async Task PutRejectsInvalidProgress(string sector, int tier)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var response = await client.PutAsJsonAsync(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            new UpdateOnslaughtProgressRequest(new(sector, tier), new("Stone", 1), new("Stone", 1), 0),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutRejectsStaleRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var current = await client.GetFromJsonAsync<OnslaughtProgressResponse>(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            TestContext.Current.CancellationToken);

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            new UpdateOnslaughtProgressRequest(
                new("Stone", 1), new("Stone", 1), new("Stone", 1), current!.Revision - 1),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
