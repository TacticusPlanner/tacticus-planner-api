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
        new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 5, false, 0)),
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
    public async Task UpdateProjectRenamesAndArchivesContainer()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/me/projects",
            new CreateProjectRequest("Event Prep", null, null),
            TestContext.Current.CancellationToken
        );
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectSummaryResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(project);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/me/projects/{project.ProjectId}",
            new UpdateProjectRequest("LRE Prep", "Next event", "#6366f1", "Archived", project.Revision),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<ProjectSummaryResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(updated);
        Assert.Equal("LRE Prep", updated.Name);
        Assert.Equal("Archived", updated.Status);
        Assert.True(updated.Revision > project.Revision);
    }

    [Fact]
    public async Task UpdateProjectRejectsStaleRevisionWithIssueCode()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var project = await GetDefaultProjectAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/me/projects/{project.ProjectId}",
            new UpdateProjectRequest("Changed", null, null, "Active", project.Revision + 1),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var conflict = await response.Content.ReadFromJsonAsync<ProjectConflictResponse>(TestContext.Current.CancellationToken);
        Assert.Equal("staleRevision", conflict?.IssueCode);
    }

    [Fact]
    public async Task DefaultProjectCannotBeArchived()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var project = await GetDefaultProjectAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/me/projects/{project.ProjectId}",
            new UpdateProjectRequest(project.Name, null, null, "Archived", project.Revision),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var conflict = await response.Content.ReadFromJsonAsync<ProjectConflictResponse>(TestContext.Current.CancellationToken);
        Assert.Equal("defaultProjectCannotBeArchived", conflict?.IssueCode);
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

    [Fact]
    public async Task ListProjectGoalsReturnsMembersOrderedByPriority()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var first = await CreateGoalAsync(client);
        var second = await CreateGoalAsync(client);

        await client.PutAsJsonAsync(
            $"/api/v1/me/projects/{defaultProject.ProjectId}/goals",
            new UpdateProjectGoalsRequest([
                new ProjectGoalEntryRequest(first.GoalId, 20),
                new ProjectGoalEntryRequest(second.GoalId, 10),
            ]),
            TestContext.Current.CancellationToken
        );

        var response = await client.GetAsync(
            $"/api/v1/me/projects/{defaultProject.ProjectId}/goals",
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ListProjectGoalsResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(2, body.Goals.Count);
        Assert.Equal(second.GoalId, body.Goals[0].Goal.GoalId);
        Assert.Equal(10, body.Goals[0].Priority);
        Assert.Equal(first.GoalId, body.Goals[1].Goal.GoalId);
        Assert.Equal(20, body.Goals[1].Priority);
    }

    [Fact]
    public async Task ListProjectGoalsForAnotherProfilesProjectIsNotFound()
    {
        var ownerClient = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ownerProject = await GetDefaultProjectAsync(ownerClient);

        var otherClient = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var response = await otherClient.GetAsync(
            $"/api/v1/me/projects/{ownerProject.ProjectId}/goals",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
