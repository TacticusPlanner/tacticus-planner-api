using System.Net;
using TacticusPlanner.Api.Features.UserSettings;

namespace TacticusPlanner.Api.IntegrationTests;

public sealed class UserSettingsEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task GetCreatesProfileDefaultsAndPutPersistsSupportedValues()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var initial = await client.GetFromJsonAsync<UserSettingsResponse>(
            "/api/v1/settings",
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(initial);
        Assert.Equal(288, initial.DailyEnergy);
        Assert.Equal(XpBookRarity.Legendary, initial.XpBookRarity);
        Assert.Equal(GoalsView.Cards, initial.GoalsView);
        Assert.Equal(1, initial.Revision);

        var response = await client.PutAsJsonAsync(
            "/api/v1/settings",
            new UpdateUserSettingsRequest(538, XpBookRarity.Epic, GoalsView.Table, initial.Revision),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<UserSettingsResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(updated);
        Assert.Equal(538, updated.DailyEnergy);
        Assert.Equal(XpBookRarity.Epic, updated.XpBookRarity);
        Assert.Equal(GoalsView.Table, updated.GoalsView);
        Assert.Equal(2, updated.Revision);

        var reread = await client.GetFromJsonAsync<UserSettingsResponse>(
            "/api/v1/settings",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(updated, reread);
    }

    [Fact]
    public async Task PutChangingOnlyTheGoalsViewKeepsTheOtherSettings()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var initial = (await client.GetFromJsonAsync<UserSettingsResponse>(
            "/api/v1/settings",
            TestContext.Current.CancellationToken
        ))!;
        var seeded = await client.PutAsJsonAsync(
            "/api/v1/settings",
            new UpdateUserSettingsRequest(438, XpBookRarity.Epic, GoalsView.Cards, initial.Revision),
            TestContext.Current.CancellationToken
        );
        var stored = (await seeded.Content.ReadFromJsonAsync<UserSettingsResponse>(TestContext.Current.CancellationToken))!;

        var response = await client.PutAsJsonAsync(
            "/api/v1/settings",
            new UpdateUserSettingsRequest(438, XpBookRarity.Epic, GoalsView.Table, stored.Revision),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();

        var reread = await client.GetFromJsonAsync<UserSettingsResponse>(
            "/api/v1/settings",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(reread);
        Assert.Equal(438, reread.DailyEnergy);
        Assert.Equal(XpBookRarity.Epic, reread.XpBookRarity);
        Assert.Equal(GoalsView.Table, reread.GoalsView);
        Assert.Equal(stored.Revision + 1, reread.Revision);
    }

    [Fact]
    public async Task PutRejectsUnsupportedEnergy()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PutAsJsonAsync(
            "/api/v1/settings",
            new UpdateUserSettingsRequest(300, XpBookRarity.Legendary, GoalsView.Cards, 0),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("Godly")]
    [InlineData("")]
    public async Task PutRejectsUnsupportedXpBookRarity(string xpBookRarity)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.SendRawJsonAsync(
            HttpMethod.Put,
            "/api/v1/settings",
            $$"""{"dailyEnergy":288,"xpBookRarity":"{{xpBookRarity}}","goalsView":"Cards","revision":0}""");

        await response.AssertFieldRejectedAsync("xpBookRarity");

        var unchanged = await client.GetFromJsonAsync<UserSettingsResponse>(
            "/api/v1/settings",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(unchanged);
        Assert.Equal(XpBookRarity.Legendary, unchanged.XpBookRarity);
        Assert.Equal(1, unchanged.Revision);
    }

    [Fact]
    public async Task PutRejectsMissingXpBookRarity()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var payload = new { DailyEnergy = 288, GoalsView = "Cards", Revision = 0 };
        var response = await client.PutAsJsonAsync(
            "/api/v1/settings",
            payload,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var unchanged = await client.GetFromJsonAsync<UserSettingsResponse>(
            "/api/v1/settings",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(unchanged);
        Assert.Equal(XpBookRarity.Legendary, unchanged.XpBookRarity);
        Assert.Equal(1, unchanged.Revision);
    }

    [Theory]
    [InlineData("Grid")]
    [InlineData("")]
    public async Task PutRejectsUnsupportedGoalsView(string goalsView)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.SendRawJsonAsync(
            HttpMethod.Put,
            "/api/v1/settings",
            $$"""{"dailyEnergy":288,"xpBookRarity":"Legendary","goalsView":"{{goalsView}}","revision":0}""");

        await response.AssertFieldRejectedAsync("goalsView");

        var unchanged = await client.GetFromJsonAsync<UserSettingsResponse>(
            "/api/v1/settings",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(unchanged);
        Assert.Equal(GoalsView.Cards, unchanged.GoalsView);
        Assert.Equal(1, unchanged.Revision);
    }

    [Fact]
    public async Task PutRejectsMissingGoalsView()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.SendRawJsonAsync(
            HttpMethod.Put,
            "/api/v1/settings",
            """{"dailyEnergy":288,"xpBookRarity":"Legendary","revision":0}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var unchanged = await client.GetFromJsonAsync<UserSettingsResponse>(
            "/api/v1/settings",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(unchanged);
        Assert.Equal(GoalsView.Cards, unchanged.GoalsView);
        Assert.Equal(1, unchanged.Revision);
    }

    [Fact]
    public async Task PutRejectsAStaleRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var current = await client.GetFromJsonAsync<UserSettingsResponse>(
            "/api/v1/settings",
            TestContext.Current.CancellationToken
        );

        var response = await client.PutAsJsonAsync(
            "/api/v1/settings",
            new UpdateUserSettingsRequest(378, XpBookRarity.Legendary, GoalsView.Table, current!.Revision - 1),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var unchanged = await client.GetFromJsonAsync<UserSettingsResponse>(
            "/api/v1/settings",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(current, unchanged);
    }
}
