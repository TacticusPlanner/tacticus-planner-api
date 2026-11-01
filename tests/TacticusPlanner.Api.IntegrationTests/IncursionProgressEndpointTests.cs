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

        Assert.Empty(initial!.Progress);
    }

    [Fact]
    public async Task PutPersistsProgressDropsZerosAndPreservesOtherOverrides()
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
        var response = await client.PutAsJsonAsync(Path, new UpdateIncursionProgressRequest(
            [new("tauBroadside", 14), new("necroReanimator", 3), new("blackForgefiend", 0)],
            initial!.Revision), ct);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(ct));
        var saved = await response.Content.ReadFromJsonAsync<IncursionProgressOverridesResponse>(ct);
        Assert.Equal([new("tauBroadside", 14), new("necroReanimator", 3)], saved!.Progress);
        Assert.True(saved.Revision > initial.Revision);

        var reread = await client.GetFromJsonAsync<IncursionProgressOverridesResponse>(Path, ct);
        Assert.Equal(saved.Progress, reread!.Progress);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        // IgnoreQueryFilters: this scope has no HttpContext, so PlannerDbContext's global profile query
        // filter (see ApplyProfileQueryFilters) has no current profile to scope to.
        Assert.Contains(db.PlayerDataOverrides.IgnoreQueryFilters(), row => row.CampaignEventProgressOverrides.Count == 1
            && row.IncursionProgressOverrides.Count == 2);
    }

    [Fact]
    public async Task PutRejectsUnknownMowsTooManyTiersDuplicatesAndStaleRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var initial = await client.GetFromJsonAsync<IncursionProgressOverridesResponse>(Path, ct);

        var unknown = await client.PutAsJsonAsync(Path, new UpdateIncursionProgressRequest(
            [new("notAMow", 1)], initial!.Revision), ct);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var tooMany = await client.PutAsJsonAsync(Path, new UpdateIncursionProgressRequest(
            [new("tauBroadside", 15)], initial.Revision), ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);

        var negative = await client.PutAsJsonAsync(Path, new UpdateIncursionProgressRequest(
            [new("tauBroadside", -1)], initial.Revision), ct);
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);

        var duplicate = await client.PutAsJsonAsync(Path, new UpdateIncursionProgressRequest(
            [new("tauBroadside", 1), new("tauBroadside", 2)], initial.Revision), ct);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        var valid = await client.PutAsJsonAsync(Path, new UpdateIncursionProgressRequest(
            [new("tauBroadside", 1)], initial.Revision), ct);
        Assert.True(valid.IsSuccessStatusCode, await valid.Content.ReadAsStringAsync(ct));
        var stale = await client.PutAsJsonAsync(Path, new UpdateIncursionProgressRequest([], initial.Revision), ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }
}
