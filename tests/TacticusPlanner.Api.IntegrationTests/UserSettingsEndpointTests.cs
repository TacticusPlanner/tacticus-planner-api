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
        Assert.Empty(initial.Preferences);
        Assert.Equal(1, initial.Revision);

        var response = await client.PutAsJsonAsync(
            "/api/v1/settings",
            new UpdateUserSettingsRequest(538, XpBookRarity.Epic, initial.Revision),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<UserSettingsResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(updated);
        Assert.Equal(538, updated.DailyEnergy);
        Assert.Equal(XpBookRarity.Epic, updated.XpBookRarity);
        Assert.Equal(2, updated.Revision);
    }

    [Fact]
    public async Task PreferencesAreWrittenAndKeptWhenOmitted()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var initial = await client.GetFromJsonAsync<UserSettingsResponse>("/api/v1/settings", ct);
        Assert.NotNull(initial);

        var on = await PutAsync(new UpdateUserSettingsRequest(
            288, XpBookRarity.Legendary, initial.Revision,
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
        var kept = await PutAsync(new UpdateUserSettingsRequest(538, XpBookRarity.Legendary, on.Revision));
        Assert.Equal(on.Preferences, kept.Preferences);
        Assert.Equal(538, kept.DailyEnergy);

        var read = await client.GetFromJsonAsync<UserSettingsResponse>("/api/v1/settings", ct);
        Assert.Equal(on.Preferences, read!.Preferences);

        var off = await PutAsync(new UpdateUserSettingsRequest(538, XpBookRarity.Legendary, kept.Revision, Preferences: []));
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
            new UpdateUserSettingsRequest(300, XpBookRarity.Legendary, 0),
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
            $$"""{"dailyEnergy":288,"xpBookRarity":"{{xpBookRarity}}","revision":0}""");

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

        var payload = new { DailyEnergy = 288, Revision = 0 };
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
            new UpdateUserSettingsRequest(378, XpBookRarity.Legendary, current!.Revision - 1),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
