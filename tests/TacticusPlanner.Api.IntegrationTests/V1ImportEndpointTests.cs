using System.Net;
using TacticusPlanner.Api.Features.PlayerDataOverrides;
using TacticusPlanner.Api.Features.V1Import;

namespace TacticusPlanner.Api.IntegrationTests;

public sealed class V1ImportEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task ValidCredentialsImportTacticusKeyAndUserId()
    {
        var client = await CreateProvisionedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/account/v1-import",
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
            "/api/v1/account/v1-import",
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
            "/api/v1/account/v1-import",
            OnboardingRequest(FakeTacticusV1Client.UsernameWithoutTacticusKey, FakeTacticusV1Client.ValidPassword),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(ImportPartStatus.Skipped, body.PersonalTacticusApiKey.Status);
        Assert.Equal("missing_personal_api_key", body.PersonalTacticusApiKey.Code);
        Assert.Equal(ImportPartStatus.Skipped, body.TacticusUserId.Status);
        Assert.Equal("missing_tacticus_user_id", body.TacticusUserId.Code);
    }

    [Fact]
    public async Task MissingCredentialsAreRejected()
    {
        var client = await CreateProvisionedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/account/v1-import",
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
            "/api/v1/player-data-overrides/onslaught-progress",
            TestContext.Current.CancellationToken);
        var initialWrite = await client.PutAsJsonAsync(
            "/api/v1/player-data-overrides/onslaught-progress",
            new UpdateOnslaughtProgressRequest(
                new(OnslaughtSector.Bronze, 1),
                new(OnslaughtSector.Bronze, 1),
                new(OnslaughtSector.Bronze, 1),
                current!.Revision),
            TestContext.Current.CancellationToken);
        initialWrite.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/v1/account/v1-import",
            OnslaughtRequest(FakeTacticusV1Client.UsernameWithOnslaught),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(ImportPartStatus.Imported, body.OnslaughtProgress.Status);

        var progress = await client.GetFromJsonAsync<OnslaughtProgressResponse>(
            "/api/v1/player-data-overrides/onslaught-progress",
            TestContext.Current.CancellationToken);
        Assert.Equal(new(OnslaughtSector.Gold, 2, true), progress!.Imperial);
        Assert.Equal(new(OnslaughtSector.Diamond, 4, true), progress.Xenos);
        Assert.Equal(new(OnslaughtSector.Silver, 3, true), progress.Chaos);
    }

    [Fact]
    public async Task MissingOnslaughtProgressIsSkipped()
    {
        var client = await CreateProvisionedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/account/v1-import",
            OnslaughtRequest(FakeTacticusV1Client.UsernameWithoutTacticusKey),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(ImportPartStatus.Skipped, body.OnslaughtProgress.Status);
        Assert.Equal("missing_onslaught_progress", body.OnslaughtProgress.Code);
    }

    [Fact]
    public async Task InvalidOnslaughtProgressFailsWithoutReplacingExistingOverrides()
    {
        var client = await CreateProvisionedClientAsync();
        var current = await client.GetFromJsonAsync<OnslaughtProgressResponse>(
            "/api/v1/player-data-overrides/onslaught-progress",
            TestContext.Current.CancellationToken);
        var initialWrite = await client.PutAsJsonAsync(
            "/api/v1/player-data-overrides/onslaught-progress",
            new UpdateOnslaughtProgressRequest(
                new(OnslaughtSector.Iron, 2),
                new(OnslaughtSector.Gold, 3),
                new(OnslaughtSector.Stone, 4),
                current!.Revision),
            TestContext.Current.CancellationToken);
        initialWrite.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/v1/account/v1-import",
            OnslaughtRequest(FakeTacticusV1Client.UsernameWithInvalidOnslaught),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(ImportPartStatus.Failed, body.OnslaughtProgress.Status);
        Assert.Equal("invalid_onslaught_progress", body.OnslaughtProgress.Code);

        var progress = await client.GetFromJsonAsync<OnslaughtProgressResponse>(
            "/api/v1/player-data-overrides/onslaught-progress",
            TestContext.Current.CancellationToken);
        Assert.Equal(new(OnslaughtSector.Iron, 2, true), progress!.Imperial);
        Assert.Equal(new(OnslaughtSector.Gold, 3, true), progress.Xenos);
        Assert.Equal(new(OnslaughtSector.Stone, 4, true), progress.Chaos);
    }

    [Fact]
    public async Task CampaignEventImportMergesRegularProgressAndPreservesChallengeIds()
    {
        var client = await CreateProvisionedClientAsync();
        var ct = TestContext.Current.CancellationToken;
        var current = await client.GetFromJsonAsync<CampaignEventProgressOverridesResponse>(
            "/api/v1/player-data-overrides/campaign-events-progress", ct);
        var initialWrite = await client.PutAsJsonAsync(
            "/api/v1/player-data-overrides/campaign-events-progress",
            new UpdateCampaignEventProgressRequest(
            [
                new("eventCampaign1", "Standard", 1, ["AMSC25B"]),
                new("eventCampaign2", "Standard", 4, ["TSC3B"]),
            ], current!.Revision), ct);
        initialWrite.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/v1/account/v1-import",
            CampaignEventRequest(FakeTacticusV1Client.UsernameWithCampaignEvents), ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(ct);
        Assert.Equal(ImportPartStatus.Imported, body!.CampaignEventProgress.Status);
        Assert.Equal("challenge_progress_not_imported", body.CampaignEventProgress.Code);

        var saved = await client.GetFromJsonAsync<CampaignEventProgressOverridesResponse>(
            "/api/v1/player-data-overrides/campaign-events-progress", ct);
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
            "/api/v1/account/v1-import",
            CampaignEventRequest(FakeTacticusV1Client.UsernameWithInvalidCampaignEvents), ct);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(ct);
        Assert.Equal(ImportPartStatus.Failed, body!.CampaignEventProgress.Status);
        Assert.Equal("invalid_campaign_event_progress", body.CampaignEventProgress.Code);
    }

    private async Task<HttpClient> CreateProvisionedClientAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, NewSubject());

        await client.GetAsync("/api/v1/account", TestContext.Current.CancellationToken);

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
