using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.UserSettings;

namespace TacticusPlanner.Api.Tests;

public sealed class UserSettingsEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task GetCreatesProfileDefaultsAndPutPersistsSupportedValues()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var initial = await client.GetFromJsonAsync<UserSettingsResponse>(
            "/api/v1/me/user-settings",
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(initial);
        Assert.Equal(288, initial.DailyEnergy);
        Assert.Equal(1, initial.Revision);

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/user-settings",
            new UpdateUserSettingsRequest(538, initial.Revision),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<UserSettingsResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(updated);
        Assert.Equal(538, updated.DailyEnergy);
        Assert.Equal(2, updated.Revision);
    }

    [Fact]
    public async Task PutRejectsUnsupportedEnergy()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/user-settings",
            new UpdateUserSettingsRequest(300, 0),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutRejectsAStaleRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var current = await client.GetFromJsonAsync<UserSettingsResponse>(
            "/api/v1/me/user-settings",
            TestContext.Current.CancellationToken
        );

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/user-settings",
            new UpdateUserSettingsRequest(378, current!.Revision - 1),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
