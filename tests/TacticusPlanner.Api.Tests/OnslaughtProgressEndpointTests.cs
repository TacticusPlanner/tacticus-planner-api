using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.PlayerDataOverrides;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Tests;

public sealed class OnslaughtProgressEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task GetReportsUnsetDefaultsAndPutPersistsAllAlliances()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var initial = await client.GetFromJsonAsync<OnslaughtProgressResponse>(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            TestContext.Current.CancellationToken);

        Assert.NotNull(initial);
        Assert.Equal(new("Stone", 1, false), initial.Imperial);
        Assert.Equal(new("Stone", 1, false), initial.Xenos);
        Assert.Equal(new("Stone", 1, false), initial.Chaos);

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            new UpdateOnslaughtProgressRequest(
                new("Gold", 2),
                new("Diamond", 4),
                new("Silver", 1),
                initial.Revision),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<OnslaughtProgressResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(updated);
        Assert.Equal(new("Gold", 2, true), updated.Imperial);
        Assert.Equal(new("Diamond", 4, true), updated.Xenos);
        Assert.Equal(new("Silver", 1, true), updated.Chaos);
        Assert.True(updated.Revision > initial.Revision);

        var reloaded = await client.GetFromJsonAsync<OnslaughtProgressResponse>(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            TestContext.Current.CancellationToken);
        Assert.NotNull(reloaded);
        Assert.Equal(new("Gold", 2, true), reloaded.Imperial);
        Assert.Equal(new("Diamond", 4, true), reloaded.Xenos);
        Assert.Equal(new("Silver", 1, true), reloaded.Chaos);
    }

    [Fact]
    public async Task PutPreservesOtherPlayerDataOverrides()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var initial = await client.GetFromJsonAsync<OnslaughtProgressResponse>(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            TestContext.Current.CancellationToken);
        Assert.NotNull(initial);

        var initializeResponse = await client.PutAsJsonAsync(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            new UpdateOnslaughtProgressRequest(
                new(initial.Imperial.Sector, initial.Imperial.Tier),
                new(initial.Xenos.Sector, initial.Xenos.Tier),
                new(initial.Chaos.Sector, initial.Chaos.Tier),
                initial.Revision),
            TestContext.Current.CancellationToken);
        initializeResponse.EnsureSuccessStatusCode();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
            // IgnoreQueryFilters: this scope has no HttpContext, so PlannerDbContext's global profile
            // query filter (see ApplyProfileQueryFilters) has no current profile to scope to.
            var row = db.PlayerDataOverrides.IgnoreQueryFilters().OrderByDescending(item => item.CreatedAt).First();
            row.CampaignEventProgressOverrides.Add(new()
            {
                CampaignGroupId = CampaignId.From("eventCampaign1"),
                Type = "Standard",
                CompletedBattleCount = 12,
            });
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
            verificationDb.PlayerDataOverrides.IgnoreQueryFilters(),
            item => item.CampaignEventProgressOverrides.Count == 1 && item.OnslaughtProgressOverrides.Count == 3);
    }

    [Fact]
    public async Task PutWithNullAllianceLeavesItUnset()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var current = await client.GetFromJsonAsync<OnslaughtProgressResponse>(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            TestContext.Current.CancellationToken);

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            new UpdateOnslaughtProgressRequest(new("Gold", 2), null, new("Iron", 4), current!.Revision),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<OnslaughtProgressResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(updated);
        Assert.Equal(new("Gold", 2, true), updated.Imperial);
        Assert.Equal(new("Stone", 1, false), updated.Xenos);
        Assert.Equal(new("Iron", 4, true), updated.Chaos);
    }

    [Theory]
    [InlineData("Wood", 1)]
    [InlineData("Stone", 0)]
    [InlineData("Stone", 5)]
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

        var firstUpdate = await client.PutAsJsonAsync(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            new UpdateOnslaughtProgressRequest(
                new("Stone", 1), new("Stone", 1), new("Stone", 1), current!.Revision),
            TestContext.Current.CancellationToken);
        firstUpdate.EnsureSuccessStatusCode();
        var saved = await firstUpdate.Content.ReadFromJsonAsync<OnslaughtProgressResponse>(TestContext.Current.CancellationToken);
        // A saved Stone 1 must be distinguishable from the unset placeholder.
        Assert.Equal(new("Stone", 1, true), saved!.Imperial);

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            new UpdateOnslaughtProgressRequest(
                new("Stone", 1), new("Stone", 1), new("Stone", 1), current.Revision),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
