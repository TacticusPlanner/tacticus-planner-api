using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.PlayerDataOverrides;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Tests;

public sealed class CampaignEventProgressEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string Path = "/api/v1/me/player-data-overrides/campaign-events-progress";

    [Fact]
    public async Task PutPersistsRegularAndExactChallengeProgressAndPreservesOnslaught()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var onslaught = await client.GetFromJsonAsync<OnslaughtProgressResponse>(
            "/api/v1/me/player-data-overrides/onslaught-progress", ct);
        var onslaughtResponse = await client.PutAsJsonAsync(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            new UpdateOnslaughtProgressRequest(new("Gold", 2), new("Stone", 1), new("Iron", 3), onslaught!.Revision), ct);
        onslaughtResponse.EnsureSuccessStatusCode();

        var initial = await client.GetFromJsonAsync<CampaignEventProgressOverridesResponse>(Path, ct);
        var response = await client.PutAsJsonAsync(Path, new UpdateCampaignEventProgressRequest(
            [new("eventCampaign1", "Standard", 12, ["AMSC25B", "AMSC3B"])],
            initial!.Revision), ct);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(ct));
        var saved = await response.Content.ReadFromJsonAsync<CampaignEventProgressOverridesResponse>(ct);
        var progress = Assert.Single(saved!.Progress);
        Assert.Equal(12, progress.CompletedBattleCount);
        Assert.Equal(["AMSC25B", "AMSC3B"], progress.CompletedChallengeBattlesIds);
        Assert.True(saved.Revision > initial.Revision);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        // IgnoreQueryFilters: this scope has no HttpContext, so PlannerDbContext's global profile query
        // filter (see ApplyProfileQueryFilters) has no current profile to scope to.
        Assert.Contains(db.PlayerDataOverrides.IgnoreQueryFilters(), row => row.OnslaughtProgressOverrides.Count == 3);
    }

    [Theory]
    [InlineData("AMS01", "Standard")]
    [InlineData("TSC3B", "Standard")]
    [InlineData("AMSC3B", "Extremis")]
    public async Task PutRejectsNonChallengeCrossEventAndCrossTypeIds(string challengeId, string type)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var initial = await client.GetFromJsonAsync<CampaignEventProgressOverridesResponse>(Path, ct);

        var response = await client.PutAsJsonAsync(Path, new UpdateCampaignEventProgressRequest(
            [new("eventCampaign1", type, null, [challengeId])], initial!.Revision), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutRejectsDuplicatesInvalidCountsAndStaleRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var initial = await client.GetFromJsonAsync<CampaignEventProgressOverridesResponse>(Path, ct);
        var duplicate = await client.PutAsJsonAsync(Path, new UpdateCampaignEventProgressRequest(
            [new("eventCampaign1", "Standard", null, ["AMSC3B", "AMSC3B"])], initial!.Revision), ct);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        var invalidCount = await client.PutAsJsonAsync(Path, new UpdateCampaignEventProgressRequest(
            [new("eventCampaign1", "Standard", 31, null)], initial.Revision), ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalidCount.StatusCode);

        var valid = await client.PutAsJsonAsync(Path, new UpdateCampaignEventProgressRequest(
            [new("eventCampaign1", "Standard", 1, null)], initial.Revision), ct);
        Assert.True(valid.IsSuccessStatusCode, await valid.Content.ReadAsStringAsync(ct));
        var stale = await client.PutAsJsonAsync(Path, new UpdateCampaignEventProgressRequest([], initial.Revision), ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }
}
