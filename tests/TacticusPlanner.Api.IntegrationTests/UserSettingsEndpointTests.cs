using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.UserSettings;

namespace TacticusPlanner.Api.IntegrationTests;

public sealed class UserSettingsEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static readonly string[] UnknownPreferences = ["NotAPreference"];

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
        Assert.Empty(initial.Preferences);
        Assert.Equal(1, initial.Revision);

        var response = await client.PutAsJsonAsync(
            "/api/v1/settings",
            new UpdateUserSettingsRequest(538, XpBookRarity.Epic, initial.Revision, GoalsView.Table),
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
            new UpdateUserSettingsRequest(438, XpBookRarity.Epic, initial.Revision, GoalsView.Cards),
            TestContext.Current.CancellationToken
        );
        var stored = (await seeded.Content.ReadFromJsonAsync<UserSettingsResponse>(TestContext.Current.CancellationToken))!;

        var response = await client.PutAsJsonAsync(
            "/api/v1/settings",
            new UpdateUserSettingsRequest(438, XpBookRarity.Epic, stored.Revision, GoalsView.Table),
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
    public async Task PreferencesAreWrittenAndKeptWhenOmitted()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var initial = await client.GetFromJsonAsync<UserSettingsResponse>("/api/v1/settings", ct);
        Assert.NotNull(initial);

        var on = await PutAsync(new UpdateUserSettingsRequest(
            288, XpBookRarity.Legendary, initial.Revision, GoalsView.Cards,
            Preferences:
            [
                UserPreference.LegendaryEventSkipDailyAds,
                UserPreference.LegendaryEventShowPaidOptions,
                UserPreference.LegendaryEventSkipDailyAds,
            ]));
        Assert.Equal(
            [UserPreference.LegendaryEventShowPaidOptions, UserPreference.LegendaryEventSkipDailyAds],
            on.Preferences);

        // A write without the field (an older client) keeps the stored preferences.
        var kept = await PutAsync(new UpdateUserSettingsRequest(538, XpBookRarity.Legendary, on.Revision, GoalsView.Cards));
        Assert.Equal(on.Preferences, kept.Preferences);
        Assert.Equal(538, kept.DailyEnergy);

        var read = await client.GetFromJsonAsync<UserSettingsResponse>("/api/v1/settings", ct);
        Assert.Equal(on.Preferences, read!.Preferences);

        var off = await PutAsync(new UpdateUserSettingsRequest(538, XpBookRarity.Legendary, kept.Revision, GoalsView.Cards, Preferences: []));
        Assert.Empty(off.Preferences);

        async Task<UserSettingsResponse> PutAsync(UpdateUserSettingsRequest request)
        {
            var response = await client.PutAsJsonAsync("/api/v1/settings", request, ct);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<UserSettingsResponse>(ct))!;
        }
    }

    [Fact]
    public async Task PutRejectsAnUnknownPreference()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var initial = await client.GetFromJsonAsync<UserSettingsResponse>("/api/v1/settings", ct);

        var response = await client.PutAsync(
            "/api/v1/settings",
            JsonContent.Create(new
            {
                dailyEnergy = 288,
                xpBookRarity = "Legendary",
                revision = initial!.Revision,
                preferences = UnknownPreferences,
            }),
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutRejectsUnsupportedEnergy()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PutAsJsonAsync(
            "/api/v1/settings",
            new UpdateUserSettingsRequest(300, XpBookRarity.Legendary, 0, GoalsView.Cards),
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

    [Theory]
    [InlineData("")]
    [InlineData(""","goalsView":null""")]
    public async Task PutWithoutGoalsViewKeepsTheStoredOne(string goalsViewJson)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var initial = await client.GetFromJsonAsync<UserSettingsResponse>(
            "/api/v1/settings",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(initial);

        var saved = await client.SendRawJsonAsync(
            HttpMethod.Put,
            "/api/v1/settings",
            $$"""{"dailyEnergy":288,"xpBookRarity":"Legendary","goalsView":"Table","revision":{{initial.Revision}}}""");
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var stored = await saved.Content.ReadFromJsonAsync<UserSettingsResponse>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(stored);

        var response = await client.SendRawJsonAsync(
            HttpMethod.Put,
            "/api/v1/settings",
            $$"""{"dailyEnergy":438,"xpBookRarity":"Legendary"{{goalsViewJson}},"revision":{{stored.Revision}}}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reread = await client.GetFromJsonAsync<UserSettingsResponse>(
            "/api/v1/settings",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(reread);
        Assert.Equal(438, reread.DailyEnergy);
        Assert.Equal(GoalsView.Table, reread.GoalsView);
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
            new UpdateUserSettingsRequest(378, XpBookRarity.Legendary, current!.Revision - 1, GoalsView.Table),
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
