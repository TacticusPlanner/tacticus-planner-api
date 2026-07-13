using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;

namespace TacticusPlanner.Api.Tests;

public sealed class ProjectsEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static readonly CreateGoalRequest RankGoal = new(
        "character",
        "unit-1",
        "rank",
        new CreateGoalConfigRequest(RankStart: 1, RankEnd: 5),
        null
    );

    [Fact]
    public async Task ListProjectsProvisionsDefaultProjectOnFirstAccess()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.GetAsync("/api/v1/me/projects", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ListProjectsResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        var project = Assert.Single(body.Projects);
        Assert.True(project.IsDefault);
        Assert.True(project.IsActivePlan);
        Assert.Equal("My Goals", project.Name);
    }

    [Fact]
    public async Task ListProjectsIsIdempotentDoesNotDuplicateDefaultProject()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        await client.GetAsync("/api/v1/me/projects", TestContext.Current.CancellationToken);
        var response = await client.GetAsync("/api/v1/me/projects", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<ListProjectsResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Single(body.Projects);
    }

    [Fact]
    public async Task CreateProjectBlankNameIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/projects",
            new CreateProjectRequest("   ", null, null),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ActivateProjectSwitchesActivePlanFlagFromDefault()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        await client.GetAsync("/api/v1/me/projects", TestContext.Current.CancellationToken); // provisions the default

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/me/projects",
            new CreateProjectRequest("Event Prep", null, null),
            TestContext.Current.CancellationToken
        );
        var created = await createResponse.Content.ReadFromJsonAsync<ProjectSummaryResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(created);
        Assert.False(created.IsActivePlan);

        var activateResponse = await client.PostAsync(
            $"/api/v1/me/projects/{created.ProjectId}/activate",
            null,
            TestContext.Current.CancellationToken
        );
        activateResponse.EnsureSuccessStatusCode();

        var listResponse = await client.GetAsync("/api/v1/me/projects", TestContext.Current.CancellationToken);
        var list = await listResponse.Content.ReadFromJsonAsync<ListProjectsResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(list);
        Assert.Equal(2, list.Projects.Count);

        var activeProjects = list.Projects.Where(project => project.IsActivePlan).ToList();
        var activeProject = Assert.Single(activeProjects);
        Assert.Equal(created.ProjectId, activeProject.ProjectId);
    }

    [Fact]
    public async Task UpdateProjectGoalsAddsGoalWithPriority()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var goal = await CreateGoalAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/me/projects/{defaultProject.ProjectId}/goals",
            new UpdateProjectGoalsRequest([new ProjectGoalEntryRequest(goal.GoalId, 1)]),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ProjectGoalsResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        var entry = Assert.Single(body.Goals);
        Assert.Equal(goal.GoalId, entry.GoalId);
        Assert.Equal(1, entry.Priority);
    }

    [Fact]
    public async Task UpdateProjectGoalsRemovingGoalsOnlyProjectIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var goal = await CreateGoalAsync(client); // created directly into the default project

        var response = await client.PutAsJsonAsync(
            $"/api/v1/me/projects/{defaultProject.ProjectId}/goals",
            new UpdateProjectGoalsRequest([]),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _ = goal;
    }

    [Fact]
    public async Task UpdateProjectGoalsStatusBulkPausesActiveGoalsOnly()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var activeGoal = await CreateGoalAsync(client);
        var completedGoal = await CreateGoalAsync(client);

        var completeResponse = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{completedGoal.GoalId}/status",
            new UpdateGoalStatusRequest("completed"),
            TestContext.Current.CancellationToken
        );
        completeResponse.EnsureSuccessStatusCode();

        var bulkResponse = await client.PostAsJsonAsync(
            $"/api/v1/me/projects/{defaultProject.ProjectId}/goals/status",
            new UpdateProjectGoalsStatusRequest("paused"),
            TestContext.Current.CancellationToken
        );
        bulkResponse.EnsureSuccessStatusCode();
        var body = await bulkResponse.Content.ReadFromJsonAsync<ProjectGoalsStatusResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(1, body.GoalsTransitioned);

        var pausedGoal = await client.GetFromJsonAsync<GoalDetailResponse>(
            $"/api/v1/me/goals/{activeGoal.GoalId}",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(pausedGoal);
        Assert.Equal("Paused", pausedGoal.Status);

        var untouchedGoal = await client.GetFromJsonAsync<GoalDetailResponse>(
            $"/api/v1/me/goals/{completedGoal.GoalId}",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(untouchedGoal);
        Assert.Equal("Completed", untouchedGoal.Status);
    }

    private static async Task<ProjectSummaryResponse> GetDefaultProjectAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<ListProjectsResponse>(
            "/api/v1/me/projects",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(response);
        return response.Projects.Single(project => project.IsDefault);
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
