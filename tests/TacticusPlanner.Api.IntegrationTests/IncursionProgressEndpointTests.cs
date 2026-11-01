using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.PlayerDataOverrides;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.IntegrationTests;

public sealed class IncursionProgressEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string Path = "/api/v1/player-data-overrides/incursions-progress";

    [Fact]
    public async Task GetReturnsEmptyListForNewProfile()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ct = TestContext.Current.CancellationToken;

        var initial = await client.GetFromJsonAsync<IncursionProgressOverridesResponse>(Path, ct);

        Assert.Empty(initial!.Battles);
    }

    [Fact]
    public async Task PutPersistsBattleStarsDropsZerosAndPreservesOtherOverrides()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var events = await client.GetFromJsonAsync<CampaignEventProgressOverridesResponse>(
            "/api/v1/player-data-overrides/campaign-events-progress", ct);
        var eventsResponse = await client.PutAsJsonAsync(
            "/api/v1/player-data-overrides/campaign-events-progress",
            new UpdateCampaignEventProgressRequest([new("eventCampaign1", "Standard", 12, null)], events!.Revision), ct);
        eventsResponse.EnsureSuccessStatusCode();

        var initial = await client.GetFromJsonAsync<IncursionProgressOverridesResponse>(Path, ct);
        // Tiers can be played in any order, and battle ids repeat across tiers.
        var response = await client.PutAsJsonAsync(Path, new UpdateIncursionProgressRequest(
            [
                new("tauBroadside", 12, "1", 3),
                new("tauBroadside", 0, "1", 1),
                new("necroReanimator", 0, "2", 2),
                new("blackForgefiend", 0, "1", 0),
            ],
            initial!.Revision), ct);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(ct));
        var saved = await response.Content.ReadFromJsonAsync<IncursionProgressOverridesResponse>(ct);
        Assert.Equal(
            [new("tauBroadside", 12, "1", 3), new("tauBroadside", 0, "1", 1), new("necroReanimator", 0, "2", 2)],
            saved!.Battles);
        Assert.True(saved.Revision > initial.Revision);

        var reread = await client.GetFromJsonAsync<IncursionProgressOverridesResponse>(Path, ct);
        Assert.Equal(saved.Battles, reread!.Battles);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        // IgnoreQueryFilters: this scope has no HttpContext, so PlannerDbContext's global profile query
        // filter (see ApplyProfileQueryFilters) has no current profile to scope to.
        Assert.Contains(db.PlayerDataOverrides.IgnoreQueryFilters(), row => row.CampaignEventProgressOverrides.Count == 1
            && row.IncursionBattleProgressOverrides.Count == 3);
    }

    [Fact]
    public async Task PutRejectsUnknownBattlesBadStarsDuplicatesNullsAndStaleRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var initial = await client.GetFromJsonAsync<IncursionProgressOverridesResponse>(Path, ct);

        UpdateIncursionProgressRequest[] invalid =
        [
            new([new("notAMow", 0, "1", 1)], initial!.Revision),
            new([new("tauBroadside", 99, "1", 1)], initial.Revision),
            new([new("tauBroadside", 0, "notABattle", 1)], initial.Revision),
            new([new("tauBroadside", 0, "1", 4)], initial.Revision),
            new([new("tauBroadside", 0, "1", -1)], initial.Revision),
            new([new("tauBroadside", 0, "1", 1), new("tauBroadside", 0, "1", 2)], initial.Revision),
        ];
        foreach (var request in invalid)
        {
            var rejected = await client.PutAsJsonAsync(Path, request, ct);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        using var nullEntryBody = new StringContent(
            $"{{\"battles\":[null],\"revision\":{initial.Revision}}}", System.Text.Encoding.UTF8, "application/json");
        var nullEntry = await client.PutAsync(Path, nullEntryBody, ct);
        Assert.Equal(HttpStatusCode.BadRequest, nullEntry.StatusCode);

        var valid = await client.PutAsJsonAsync(Path, new UpdateIncursionProgressRequest(
            [new("tauBroadside", 0, "1", 1)], initial.Revision), ct);
        Assert.True(valid.IsSuccessStatusCode, await valid.Content.ReadAsStringAsync(ct));
        var stale = await client.PutAsJsonAsync(Path, new UpdateIncursionProgressRequest([], initial.Revision), ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }
}
