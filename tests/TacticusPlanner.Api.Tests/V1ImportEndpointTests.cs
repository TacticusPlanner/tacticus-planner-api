using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.PlayerDataOverrides;
using TacticusPlanner.Api.Features.V1Import;

namespace TacticusPlanner.Api.Tests;

public sealed class V1ImportEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public void ParsesCurrentV1OnslaughtPreferencesShapeAndNormalizesSectorNames()
    {
        using var document = JsonDocument.Parse("""
            {
              "onslaughtPreferences": {
                "Imperial": { "sector": "gold", "tier": 2 },
                "Xenos": { "sector": "diamond", "tier": 4 },
                "Chaos": { "sector": "silver", "tier": 3 }
              }
            }
            """);

        var result = TacticusV1Client.ReadOnslaughtProgress(document.RootElement);

        Assert.True(result.IsPresent);
        Assert.NotNull(result.Progress);
        Assert.Equal(new("Gold", 2), result.Progress.Imperial);
        Assert.Equal(new("Diamond", 4), result.Progress.Xenos);
        Assert.Equal(new("Silver", 3), result.Progress.Chaos);
    }

    [Fact]
    public void ParsesOnlyRegularCampaignEventProgressFromCurrentV1Shape()
    {
        using var document = JsonDocument.Parse("""
            {
              "campaignsProgress": {
                "Adeptus Mechanicus Standard": 12,
                "Adeptus Mechanicus Standard Challenge": 2,
                "Adeptus Mechanicus Extremis": 7,
                "Indomitus": 75
              }
            }
            """);

        var result = TacticusV1Client.ReadCampaignEventProgress(document.RootElement);

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

    [Fact]
    public async Task GoalsOnlyImportReturnsSpecsForTheClientToCreateAndReportsUnsupportedGoals()
    {
        var client = await CreateProvisionedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            new ImportV1ProfileRequest(
                FakeTacticusV1Client.UsernameWithGoals,
                FakeTacticusV1Client.ValidPassword,
                new ImportV1Selection(false, false, false, true, false, false)
            ),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal("Imported", body.Goals.Status);
        var spec = Assert.Single(body.GoalSpecs);
        Assert.Equal("Character", spec.EntityType);
        Assert.Equal("ultraInceptorSgt", spec.EntityId);
        var goalSpec = Assert.Single(spec.Goals);
        Assert.Equal("Rank", goalSpec.GoalType);
        Assert.Equal(1, body.GoalsSkipped);
        Assert.Contains(body.GoalIssues, issue => issue.Code == "unsupported_goal_type");
        Assert.Equal("not_selected", body.PersonalTacticusApiKey.Code);
        Assert.Equal("not_selected", body.OnslaughtProgress.Code);

        // Nothing was created server-side — this is a pure translation. The caller (the web client, in
        // production) is responsible for submitting the returned specs through POST me/goals/combined.
        var goals = await client.GetFromJsonAsync<ListGoalsResponse>(
            "/api/v1/me/goals",
            TestContext.Current.CancellationToken
        );
        Assert.Empty(goals!.Goals);
    }

    [Fact]
    public async Task GoalsOnlyImportSkipsCandidatesThatAlreadyHaveAMatchingGoal()
    {
        var client = await CreateProvisionedClientAsync();
        var nativeResponse = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            new CreateGoalRequest(
                "character",
                "ultraInceptorSgt",
                "rank",
                new CreateGoalConfigRequest(
                    Rank: new RankTargetRequest(0, false, 0, 1, false, 0)
                ),
                null
            ),
            TestContext.Current.CancellationToken
        );
        nativeResponse.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            new ImportV1ProfileRequest(
                FakeTacticusV1Client.UsernameWithGoals,
                FakeTacticusV1Client.ValidPassword,
                new ImportV1Selection(false, false, false, true, false, false)
            ),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Empty(body.GoalSpecs);
        // 1 unsupported goal + 1 skipped as already-existing.
        Assert.Equal(2, body.GoalsSkipped);
        Assert.Contains(body.GoalIssues, issue => issue.Code == "goal_already_exists");
        Assert.Contains(body.GoalIssues, issue => issue.Code == "unsupported_goal_type");

        var goals = await client.GetFromJsonAsync<ListGoalsResponse>(
            "/api/v1/me/goals",
            TestContext.Current.CancellationToken
        );
        var existing = Assert.Single(goals!.Goals);
        Assert.Equal("ultraInceptorSgt", existing.EntityId);
    }

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
