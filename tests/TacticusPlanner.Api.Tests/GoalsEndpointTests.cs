using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.Tests;

public sealed class GoalsEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static readonly CreateGoalRequest RankGoal = new(
        "character",
        "unit-1",
        "rank",
        new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 5, false, 0)),
        null
    );

    [Fact]
    public async Task UnauthenticatedListIsRejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.NoAuthHeader, "1");

        var response = await client.GetAsync("/api/v1/me/goals", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnprovisionedAccountListIsNotFound()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, $"goals-{Guid.NewGuid()}");

        var response = await client.GetAsync("/api/v1/me/goals", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateGoalThenAppearsInList()
    {
        // GoalsTestHelpers only hits GET /api/v1/me — no ListProjects call — so a goal landing Active in
        // its default project here also proves the default project is seeded during account provisioning,
        // not just lazily by the Projects list endpoint.
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal,
            TestContext.Current.CancellationToken
        );
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(created);
        Assert.Equal("Character", created.EntityType);
        Assert.Equal("Rank", created.GoalType);
        Assert.Equal("Active", created.Status);
        Assert.Single(created.Events);
        Assert.Equal(GoalEventType.Created, created.Events[0].Type);

        var listResponse = await client.GetAsync("/api/v1/me/goals", TestContext.Current.CancellationToken);
        var list = await listResponse.Content.ReadFromJsonAsync<ListGoalsResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(list);
        Assert.Contains(list.Goals, goal => goal.GoalId == created.GoalId);
    }

    [Fact]
    public async Task CreateGoalInNonActiveProjectStartsPaused()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var otherProjectResponse = await client.PostAsJsonAsync(
            "/api/v1/me/projects",
            new CreateProjectRequest("Event Prep", null, null),
            TestContext.Current.CancellationToken
        );
        var otherProject = await otherProjectResponse.Content.ReadFromJsonAsync<ProjectSummaryResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(otherProject);
        Assert.False(otherProject.IsActivePlan);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal with { ProjectId = otherProject.ProjectId },
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(created);
        Assert.Equal("Paused", created.Status);
    }

    [Fact]
    public async Task CreateGoalUnknownEntityTypeIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal with { EntityType = "spaceship" },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("upgrade", "rank")]
    [InlineData("character", "material")]
    public async Task CreateGoalDeferredGoalTypeOrEntityTypeIsRejected(string entityType, string goalType)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal with { EntityType = entityType, GoalType = goalType },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateGoalChangesNotesAndFarmingLocationOverride()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}",
            new UpdateGoalRequest("remember to farm epic first", [CampaignBattleId.From("node-1")]),
            TestContext.Current.CancellationToken
        );
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(updated);
        Assert.Equal("remember to farm epic first", updated.Notes);
        Assert.Equal(["node-1"], updated.Config.FarmingLocationIds);
        // The immutable target fields must be untouched by an edit.
        Assert.NotNull(created.Config.Rank);
        Assert.NotNull(updated.Config.Rank);
        Assert.Equal(created.Config.Rank.Start, updated.Config.Rank.Start);
        Assert.Equal(created.Config.Rank.End, updated.Config.Rank.End);
    }

    [Fact]
    public async Task UpdateGoalStatusTransitionsAndAppendsEvent()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var pauseResponse = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}/status",
            new UpdateGoalStatusRequest("paused"),
            TestContext.Current.CancellationToken
        );
        pauseResponse.EnsureSuccessStatusCode();
        var paused = await pauseResponse.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(paused);
        Assert.Equal("Paused", paused.Status);
        Assert.Contains(paused.Events, evt => evt.Type == GoalEventType.Paused);
    }

    [Fact]
    public async Task UpdateGoalStatusUnknownStatusIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}/status",
            new UpdateGoalStatusRequest("not-a-status"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListGoalsExcludesArchivedByDefaultButArchivedQueryReturnsThem()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var archiveResponse = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}/status",
            new UpdateGoalStatusRequest("archived"),
            TestContext.Current.CancellationToken
        );
        archiveResponse.EnsureSuccessStatusCode();

        var defaultList = await client.GetFromJsonAsync<ListGoalsResponse>(
            "/api/v1/me/goals",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(defaultList);
        Assert.DoesNotContain(defaultList.Goals, goal => goal.GoalId == created.GoalId);

        var archivedList = await client.GetFromJsonAsync<ListGoalsResponse>(
            "/api/v1/me/goals?archived=true",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(archivedList);
        Assert.Contains(archivedList.Goals, goal => goal.GoalId == created.GoalId);
    }

    [Fact]
    public async Task DeleteGoalRemovesRowPermanently()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var deleteResponse = await client.DeleteAsync(
            $"/api/v1/me/goals/{created.GoalId}",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/v1/me/goals", TestContext.Current.CancellationToken);
        var list = await listResponse.Content.ReadFromJsonAsync<ListGoalsResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(list);
        Assert.DoesNotContain(list.Goals, goal => goal.GoalId == created.GoalId);

        var getResponse = await client.GetAsync(
            $"/api/v1/me/goals/{created.GoalId}",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        // The row is gone (hard delete), so a second delete is a 404, not a no-op success.
        var secondDelete = await client.DeleteAsync(
            $"/api/v1/me/goals/{created.GoalId}",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NotFound, secondDelete.StatusCode);
    }

    [Fact]
    public async Task GetGoalOwnedByAnotherProfileIsNotFound()
    {
        var ownerClient = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(ownerClient);

        var otherClient = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var response = await otherClient.GetAsync(
            $"/api/v1/me/goals/{created.GoalId}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<GoalDetailResponse> CreateGoalAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/me/goals", RankGoal, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var goal = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(goal);
        return goal;
    }
}
