using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.TacticusIntegration;
using TacticusPlanner.Api.Features.UserSettings;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary><c>api-error-contract</c>: every writable error is one problem-details shape with an issue code.</summary>
public sealed class ApiErrorContractTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] RequiredFields = ["type", "title", "status", "detail", "instance", "traceId", "issueCode"];

    private static async Task<JsonObject> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string issueCode)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.ReadProblemAsync();
        Assert.All(RequiredFields, field => Assert.NotNull(problem[field]));
        Assert.Equal((int)status, problem["status"]!.GetValue<int>());
        Assert.Equal(issueCode, problem["issueCode"]!.GetValue<string>());
        Assert.Equal(response.RequestMessage!.RequestUri!.AbsolutePath, problem["instance"]!.GetValue<string>());
        return problem;
    }

    [Fact]
    public async Task AValidationFailureNamesEachOffendingField()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync("/api/v1/goals/status", new UpdateGoalsStatusRequest([], GoalStatus.Active, -1), Ct);

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        var errors = problem["errors"]!.AsObject();
        Assert.NotNull(errors["goalIds"]);
        Assert.NotNull(errors["expectedOrderRevision"]);
    }

    [Fact]
    public async Task AnotherProfilesGoalIsNotFound()
    {
        var owner = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var goal = await owner.CreateGoalAsync(new CreateGoalRequest(GoalEntityType.Character, "blackTerminator", GoalType.Unlock, new(), null));
        var other = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        await AssertProblemAsync(await other.GetAsync($"/api/v1/goals/{goal.GoalId}", Ct), HttpStatusCode.NotFound, "notFound");
    }

    [Fact]
    public async Task FrameworkFailuresUseTheSharedWriterAndKeepTheirHeaders()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        await AssertProblemAsync(await client.GetAsync("/api/v1/no-such-route", Ct), HttpStatusCode.NotFound, "notFound");

        var notAllowed = await client.PatchAsync("/api/v1/goals", JsonContent.Create(new { }), Ct);
        await AssertProblemAsync(notAllowed, HttpStatusCode.MethodNotAllowed, "methodNotAllowed");
        Assert.NotEmpty(notAllowed.Content.Headers.Allow);

        using var anonymous = new HttpRequestMessage(HttpMethod.Get, "/api/v1/goals");
        anonymous.Headers.Add(PlannerTestAuthenticationHandler.NoAuthHeader, "1");
        var unauthorized = await client.SendAsync(anonymous, Ct);
        await AssertProblemAsync(unauthorized, HttpStatusCode.Unauthorized, "unauthorized");
        Assert.NotEmpty(unauthorized.Headers.WwwAuthenticate);

        using var noScope = new HttpRequestMessage(HttpMethod.Get, "/api/v1/goals");
        noScope.Headers.Add(PlannerTestAuthenticationHandler.OmitScopeHeader, "1");
        await AssertProblemAsync(await client.SendAsync(noScope, Ct), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task ASettingsConflictCarriesTheCurrentSettingsInTheirReadShape()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var read = (await client.GetFromJsonAsync<UserSettingsResponse>("/api/v1/settings", Ct))!;
        (await client.PutAsJsonAsync(
            "/api/v1/settings",
            new UpdateUserSettingsRequest(538, read.XpBookRarity, GoalsView.Table, read.Revision), Ct))
            .EnsureSuccessStatusCode();

        var stale = await client.PutAsJsonAsync(
            "/api/v1/settings",
            new UpdateUserSettingsRequest(438, read.XpBookRarity, GoalsView.Cards, read.Revision), Ct);

        await AssertProblemAsync(stale, HttpStatusCode.Conflict, "concurrency");
        var current = await stale.ReadCurrentAsync<UserSettingsResponse>();
        Assert.Equal(await client.GetFromJsonAsync<UserSettingsResponse>("/api/v1/settings", Ct), current);
        Assert.Equal(538, current.DailyEnergy);
        Assert.Equal(GoalsView.Table, current.GoalsView);
    }

    [Fact]
    public async Task AnUpstreamTimeoutDuringPlayerSyncIsUpstreamUnavailable()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var token = $"timeout-{Guid.NewGuid()}";
        factory.TacticusApi.ConfigurePlayerResponse(token, FakeTacticusApi.BuildPlayerResponse());
        (await client.PutAsJsonAsync("/api/v1/account/tacticus-integration", new UpdateTacticusIntegrationRequest(token, null), Ct))
            .EnsureSuccessStatusCode();
        factory.TacticusApi.ConfigurePlayerTimeout(token);

        var response = await client.PostAsync("/api/v1/player-data/sync", null, Ct);

        var problem = await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "upstreamUnavailable");
        Assert.DoesNotContain("Exception", problem["detail"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"entityType":"character","entityId":"blackTerminator","goals":[{"goalType":"unlock","config":{}}],"startPaused":null}""")]
    [InlineData("""{"entityType":"character","entityId":"blackTerminator","goals":[{"goalType":"unlock","config":{}}],"startPaused":"yes"}""")]
    [InlineData("""{"entityType":"character","entityId":"blackTerminator","goals":[{"goalType":"unlock","config":{},"startPaused":true}]}""")]
    public async Task AnInvalidStartPausedIsRejectedNamingItAndCreatesNothing(string body)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/v1/goals", content, Ct);

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Contains(problem["errors"]!.AsObject(), error => error.Key.EndsWith("startPaused", StringComparison.Ordinal));
        Assert.Empty((await client.ListGoalsAsync()).Goals);
    }
}
