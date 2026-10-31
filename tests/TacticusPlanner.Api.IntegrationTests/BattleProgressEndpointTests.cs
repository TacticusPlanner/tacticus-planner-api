using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.PlayerDataOverrides;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.IntegrationTests;

public sealed class BattleProgressEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string Path = "/api/v1/player-data-overrides/battle-progress";

    [Fact]
    public async Task GetReturnsEmptyListForNewProfile()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ct = TestContext.Current.CancellationToken;

        var initial = await client.GetFromJsonAsync<BattleProgressOverridesResponse>(Path, ct);

        Assert.Empty(initial!.Battles);
    }

    [Fact]
    public async Task PutPersistsBattlesAndPreservesOtherOverrides()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var events = await client.GetFromJsonAsync<CampaignEventProgressOverridesResponse>(
            "/api/v1/player-data-overrides/campaign-events-progress", ct);
        var eventsResponse = await client.PutAsJsonAsync(
            "/api/v1/player-data-overrides/campaign-events-progress",
            new UpdateCampaignEventProgressRequest([new("eventCampaign1", "Standard", 12, null)], events!.Revision), ct);
        eventsResponse.EnsureSuccessStatusCode();

        var initial = await client.GetFromJsonAsync<BattleProgressOverridesResponse>(Path, ct);
        var response = await client.PutAsJsonAsync(Path, new UpdateBattleProgressRequest(
            [
                new("I06", 2, null, null),
                new("AMS01", 3, null, BattleMilestone.Silver),
                new("AME01", 3, false, BattleMilestone.None),
            ],
            initial!.Revision), ct);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(ct));
        var saved = await response.Content.ReadFromJsonAsync<BattleProgressOverridesResponse>(ct);
        Assert.Equal(["I06", "AMS01", "AME01"], saved!.Battles.Select(battle => battle.BattleId));
        Assert.Equal(BattleMilestone.Silver, saved.Battles[1].Milestone);
        Assert.Equal((false, BattleMilestone.None), (saved.Battles[2].LightningVictory, saved.Battles[2].Milestone));
        Assert.True(saved.Revision > initial.Revision);

        var reread = await client.GetFromJsonAsync<BattleProgressOverridesResponse>(Path, ct);
        Assert.Equal(2, reread!.Battles[0].Medals);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        // IgnoreQueryFilters: this scope has no HttpContext, so PlannerDbContext's global profile query
        // filter (see ApplyProfileQueryFilters) has no current profile to scope to.
        Assert.Contains(db.PlayerDataOverrides.IgnoreQueryFilters(), row => row.CampaignEventProgressOverrides.Count == 1
            && row.BattleProgressOverrides.Count == 3);
    }

    [Fact]
    public async Task PutRejectsUnknownBattlesInvalidMedalsAndStaleRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var initial = await client.GetFromJsonAsync<BattleProgressOverridesResponse>(Path, ct);

        var unknown = await client.PutAsJsonAsync(Path, new UpdateBattleProgressRequest(
            [new("NotABattle", 1, null, null)], initial!.Revision), ct);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var invalidMedals = await client.PutAsJsonAsync(Path, new UpdateBattleProgressRequest(
            [new("I06", 4, null, null)], initial.Revision), ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalidMedals.StatusCode);

        // Extremis battles award Gold only.
        var extremisBronze = await client.PutAsJsonAsync(Path, new UpdateBattleProgressRequest(
            [new("AME01", 3, null, BattleMilestone.Bronze)], initial.Revision), ct);
        Assert.Equal(HttpStatusCode.BadRequest, extremisBronze.StatusCode);

        var valid = await client.PutAsJsonAsync(Path, new UpdateBattleProgressRequest(
            [new("I06", 1, null, null)], initial.Revision), ct);
        Assert.True(valid.IsSuccessStatusCode, await valid.Content.ReadAsStringAsync(ct));
        var stale = await client.PutAsJsonAsync(Path, new UpdateBattleProgressRequest([], initial.Revision), ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }
}
