using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TacticusPlanner.Api.Features.PlayerDataOverrides;
using TacticusPlanner.Api.Features.V1Import;

namespace TacticusPlanner.Api.Tests;

public sealed class V1ImportEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ParsesCurrentV1OnslaughtPreferencesShapeAndNormalizesSectorNames()
    {
        var data = JsonSerializer.Deserialize<V1UserData>("""
            {
              "onslaughtPreferences": {
                "Imperial": { "sector": "gold", "tier": 2 },
                "Xenos": { "sector": "diamond", "tier": 4 },
                "Chaos": { "sector": "silver", "tier": 3 }
              }
            }
            """, WebJsonOptions);

        var result = TacticusV1Client.ReadOnslaughtProgress(data);

        Assert.True(result.IsPresent);
        Assert.NotNull(result.Progress);
        Assert.Equal(new("Gold", 2), result.Progress.Imperial);
        Assert.Equal(new("Diamond", 4), result.Progress.Xenos);
        Assert.Equal(new("Silver", 3), result.Progress.Chaos);
    }

    [Fact]
    public void ParsesV1ShardSourceFieldsPreviouslyDroppedEntirely()
    {
        // A captured V1 profile payload's goals array: an Ascension goal (combined onslaught + energy
        // farming, plus mythic campaign usage) and an Unlock goal (campaign usage only — V1 never sends
        // shardFarmType/mythicCampaignsUsage for Unlock).
        var data = JsonSerializer.Deserialize<V1UserData>("""
            {
              "goals": [
                {
                  "id": "ascend-1",
                  "character": "Bellator",
                  "type": 2,
                  "priority": 1,
                  "dailyRaids": true,
                  "targetRarity": 3,
                  "targetStars": 8,
                  "shardFarmType": "both",
                  "campaignsUsage": 1,
                  "mythicCampaignsUsage": 2
                },
                {
                  "id": "unlock-1",
                  "character": "Bellator",
                  "type": 3,
                  "priority": 2,
                  "dailyRaids": false,
                  "campaignsUsage": 1
                }
              ]
            }
            """, WebJsonOptions);

        Assert.NotNull(data?.Goals);
        var ascend = Assert.Single(data.Goals, goal => goal.Id == "ascend-1");
        Assert.Equal("both", ascend.ShardFarmType);
        Assert.Equal(1, ascend.CampaignsUsage);
        Assert.Equal(2, ascend.MythicCampaignsUsage);

        var unlock = Assert.Single(data.Goals, goal => goal.Id == "unlock-1");
        Assert.Null(unlock.ShardFarmType);
        Assert.Equal(1, unlock.CampaignsUsage);
        Assert.Null(unlock.MythicCampaignsUsage);
    }

    [Fact]
    public void ParsesOnlyRegularCampaignEventProgressFromCurrentV1Shape()
    {
        var data = JsonSerializer.Deserialize<V1UserData>("""
            {
              "campaignsProgress": {
                "Adeptus Mechanicus Standard": 12,
                "Adeptus Mechanicus Standard Challenge": 2,
                "Adeptus Mechanicus Extremis": 7,
                "Indomitus": 75
              }
            }
            """, WebJsonOptions);

        var result = TacticusV1Client.ReadCampaignEventProgress(data);

        Assert.True(result.IsPresent);
        Assert.Equal(2, result.Progress!.Count);
        Assert.Contains(new V1CampaignEventProgress("eventCampaign1", "Standard", 12), result.Progress);
        Assert.Contains(new V1CampaignEventProgress("eventCampaign1", "Extremis", 7), result.Progress);
    }

    [Fact]
    public async Task ValidCredentialsImportTacticusKeyAndUserId()
    {
        var client = await CreateProvisionedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            OnboardingRequest(FakeTacticusV1Client.ValidUsername, FakeTacticusV1Client.ValidPassword),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<ImportV1ProfileResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(FakeTacticusApi.PlayerName, body.PlayerName);
        Assert.NotNull(body.TacticusApiKeyMasked);
        Assert.NotNull(body.TacticusUserIdMasked);
    }

    [Fact]
    public async Task InvalidV1CredentialsAreRejected()
    {
        var client = await CreateProvisionedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            OnboardingRequest(FakeTacticusV1Client.ValidUsername, "wrong-password"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MissingSelectedPartIsReportedWithoutFailingCredentialImport()
    {
        var client = await CreateProvisionedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            OnboardingRequest(FakeTacticusV1Client.UsernameWithoutTacticusKey, FakeTacticusV1Client.ValidPassword),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("Skipped", body.PersonalTacticusApiKey.Status);
        Assert.Equal("missing_personal_api_key", body.PersonalTacticusApiKey.Code);
        Assert.Equal("Skipped", body.TacticusUserId.Status);
        Assert.Equal("missing_tacticus_user_id", body.TacticusUserId.Code);
    }

    [Fact]
    public async Task MissingCredentialsAreRejected()
    {
        var client = await CreateProvisionedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            OnboardingRequest(null, null),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Goal-import behavior (server-side creation, outcomes, prerequisites, acquisition sources,
    // ordering) is covered in V1GoalImportEndpointTests.cs — this file covers the other import parts.

    [Fact]
    public async Task OnslaughtImportReplacesAllAllianceProgressAndSupportsCompletedSectorTier()
    {
        var client = await CreateProvisionedClientAsync();
        var current = await client.GetFromJsonAsync<OnslaughtProgressResponse>(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            TestContext.Current.CancellationToken);
        var initialWrite = await client.PutAsJsonAsync(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            new UpdateOnslaughtProgressRequest(
                new("Bronze", 1),
                new("Bronze", 1),
                new("Bronze", 1),
                current!.Revision),
            TestContext.Current.CancellationToken);
        initialWrite.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            OnslaughtRequest(FakeTacticusV1Client.UsernameWithOnslaught),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("Imported", body.OnslaughtProgress.Status);

        var progress = await client.GetFromJsonAsync<OnslaughtProgressResponse>(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            TestContext.Current.CancellationToken);
        Assert.Equal(new("Gold", 2), progress!.Imperial);
        Assert.Equal(new("Diamond", 4), progress.Xenos);
        Assert.Equal(new("Silver", 3), progress.Chaos);
    }

    [Fact]
    public async Task MissingOnslaughtProgressIsSkipped()
    {
        var client = await CreateProvisionedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            OnslaughtRequest(FakeTacticusV1Client.UsernameWithoutTacticusKey),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("Skipped", body.OnslaughtProgress.Status);
        Assert.Equal("missing_onslaught_progress", body.OnslaughtProgress.Code);
    }

    [Fact]
    public async Task InvalidOnslaughtProgressFailsWithoutReplacingExistingOverrides()
    {
        var client = await CreateProvisionedClientAsync();
        var current = await client.GetFromJsonAsync<OnslaughtProgressResponse>(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            TestContext.Current.CancellationToken);
        var initialWrite = await client.PutAsJsonAsync(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            new UpdateOnslaughtProgressRequest(
                new("Iron", 2),
                new("Gold", 3),
                new("Stone", 4),
                current!.Revision),
            TestContext.Current.CancellationToken);
        initialWrite.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            OnslaughtRequest(FakeTacticusV1Client.UsernameWithInvalidOnslaught),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("Failed", body.OnslaughtProgress.Status);
        Assert.Equal("invalid_onslaught_progress", body.OnslaughtProgress.Code);

        var progress = await client.GetFromJsonAsync<OnslaughtProgressResponse>(
            "/api/v1/me/player-data-overrides/onslaught-progress",
            TestContext.Current.CancellationToken);
        Assert.Equal(new("Iron", 2), progress!.Imperial);
        Assert.Equal(new("Gold", 3), progress.Xenos);
        Assert.Equal(new("Stone", 4), progress.Chaos);
    }

    [Fact]
    public async Task CampaignEventImportMergesRegularProgressAndPreservesChallengeIds()
    {
        var client = await CreateProvisionedClientAsync();
        var ct = TestContext.Current.CancellationToken;
        var current = await client.GetFromJsonAsync<CampaignEventProgressOverridesResponse>(
            "/api/v1/me/player-data-overrides/campaign-events-progress", ct);
        var initialWrite = await client.PutAsJsonAsync(
            "/api/v1/me/player-data-overrides/campaign-events-progress",
            new UpdateCampaignEventProgressRequest(
            [
                new("eventCampaign1", "Standard", 1, ["AMSC25B"]),
                new("eventCampaign2", "Standard", 4, ["TSC3B"]),
            ], current!.Revision), ct);
        initialWrite.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            CampaignEventRequest(FakeTacticusV1Client.UsernameWithCampaignEvents), ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(ct);
        Assert.Equal("Imported", body!.CampaignEventProgress.Status);
        Assert.Equal("challenge_progress_not_imported", body.CampaignEventProgress.Code);

        var saved = await client.GetFromJsonAsync<CampaignEventProgressOverridesResponse>(
            "/api/v1/me/player-data-overrides/campaign-events-progress", ct);
        Assert.Contains(saved!.Progress, item => item.CampaignGroupId == "eventCampaign1"
            && item.Type == "Standard"
            && item.CompletedBattleCount == 12
            && item.CompletedChallengeBattlesIds!.SequenceEqual(["AMSC25B"]));
        Assert.Contains(saved.Progress, item => item.CampaignGroupId == "eventCampaign2"
            && item.CompletedBattleCount == 4
            && item.CompletedChallengeBattlesIds!.SequenceEqual(["TSC3B"]));
    }

    [Fact]
    public async Task InvalidCampaignEventImportDoesNotChangeOverrides()
    {
        var client = await CreateProvisionedClientAsync();
        var ct = TestContext.Current.CancellationToken;
        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            CampaignEventRequest(FakeTacticusV1Client.UsernameWithInvalidCampaignEvents), ct);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(ct);
        Assert.Equal("Failed", body!.CampaignEventProgress.Status);
        Assert.Equal("invalid_campaign_event_progress", body.CampaignEventProgress.Code);
    }

    private async Task<HttpClient> CreateProvisionedClientAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, NewSubject());

        await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        return client;
    }

    private static ImportV1ProfileRequest OnboardingRequest(string? username, string? password) =>
        new(username, password, new ImportV1Selection(true, true, false, false, false, false));

    private static ImportV1ProfileRequest OnslaughtRequest(string username) =>
        new(username, FakeTacticusV1Client.ValidPassword, new ImportV1Selection(false, false, false, false, true, false));

    private static ImportV1ProfileRequest CampaignEventRequest(string username) =>
        new(username, FakeTacticusV1Client.ValidPassword,
            new ImportV1Selection(false, false, false, false, false, true));

    private static string NewSubject() => $"v1-import-{Guid.NewGuid()}";
}
