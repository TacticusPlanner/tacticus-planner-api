using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.PlanningSettings;

namespace TacticusPlanner.Api.Tests;

public sealed class PlanningSettingsEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task GetCreatesProfileDefaultsAndPutPersistsSupportedValues()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var initial = await client.GetFromJsonAsync<PlanningSettingsResponse>(
            "/api/v1/me/planning-settings",
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(initial);
        Assert.Equal(288, initial.DailyEnergy);
        Assert.Equal("GoalPriority", initial.Ordering);
        Assert.Equal(1, initial.Revision);

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/planning-settings",
            new UpdatePlanningSettingsRequest(538, "TotalMaterials", initial.Revision),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<PlanningSettingsResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(updated);
        Assert.Equal(538, updated.DailyEnergy);
        Assert.Equal("TotalMaterials", updated.Ordering);
        Assert.Equal(2, updated.Revision);
    }

    [Theory]
    [InlineData(300, "GoalPriority")]
    [InlineData(288, "CheapestNode")]
    public async Task PutRejectsUnsupportedSettings(int dailyEnergy, string ordering)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/planning-settings",
            new UpdatePlanningSettingsRequest(dailyEnergy, ordering, 0),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutRejectsAStaleRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var current = await client.GetFromJsonAsync<PlanningSettingsResponse>(
            "/api/v1/me/planning-settings",
            TestContext.Current.CancellationToken
        );

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/planning-settings",
            new UpdatePlanningSettingsRequest(378, "GoalPriority", current!.Revision - 1),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
