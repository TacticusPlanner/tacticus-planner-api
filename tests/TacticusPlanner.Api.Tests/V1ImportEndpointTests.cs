using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.V1Import;

namespace TacticusPlanner.Api.Tests;

public sealed class V1ImportEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
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
                new ImportV1Selection(false, false, false, true)
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
                new ImportV1Selection(false, false, false, true)
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

    private async Task<HttpClient> CreateProvisionedClientAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, NewSubject());

        await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        return client;
    }

    private static ImportV1ProfileRequest OnboardingRequest(string? username, string? password) =>
        new(username, password, new ImportV1Selection(true, true, false, false));

    private static string NewSubject() => $"v1-import-{Guid.NewGuid()}";
}
