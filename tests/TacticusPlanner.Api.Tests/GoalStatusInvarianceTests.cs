using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// Regression guard for goal-lifecycle-status: "Project operations do not change a goal's status". Neither
/// membership changes (through either direction of the membership endpoints) nor changing which project is
/// the profile's active plan may move a goal between Active and Paused — a goal's status changes only
/// through an operation that explicitly targets it. This already holds, and these tests keep it holding.
/// </summary>
public sealed class GoalStatusInvarianceTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static readonly CreateGoalRequest RankGoal = new(
        "character",
        "blackTerminator",
        "rank",
        new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 5, false, 0)),
        null
    );

    [Fact]
    public async Task MakingAProjectCurrentDoesNotActivateItsPausedGoals()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var projectB = await CreateProjectAsync(client, "Event Prep");

        var goal = await CreateGoalAsync(client, RankGoal with
        {
            Projects = [new ProjectPriorityRequest(projectB.ProjectId)],
            StartPaused = true,
        });
        Assert.Equal("Paused", goal.Status);

        await ActivateAsync(client, projectB.ProjectId);

        Assert.Equal("Paused", (await GetGoalAsync(client, goal.GoalId)).Status);
    }

    [Fact]
    public async Task LosingCurrentPlanStandingDoesNotPauseGoals()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        Assert.True(defaultProject.IsActivePlan);

        var goal = await CreateGoalAsync(client, RankGoal with
        {
            Projects = [new ProjectPriorityRequest(defaultProject.ProjectId)],
        });
        Assert.Equal("Active", goal.Status);

        var projectB = await CreateProjectAsync(client, "Event Prep");
        await ActivateAsync(client, projectB.ProjectId);

        Assert.Equal("Active", (await GetGoalAsync(client, goal.GoalId)).Status);
    }

    [Fact]
    public async Task AddingAMembershipThroughTheGoalDoesNotChangeStatus()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var projectB = await CreateProjectAsync(client, "Event Prep");

        var goal = await CreateGoalAsync(client, RankGoal with
        {
            Projects = [new ProjectPriorityRequest(defaultProject.ProjectId)],
            StartPaused = true,
        });
        Assert.Equal("Paused", goal.Status);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/me/goals/{goal.GoalId}/projects",
            new UpdateGoalProjectsRequest([defaultProject.ProjectId, projectB.ProjectId]),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();

        Assert.Equal("Paused", (await GetGoalAsync(client, goal.GoalId)).Status);
    }

    [Fact]
    public async Task AddingAMembershipThroughTheProjectDoesNotChangeStatus()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var projectB = await CreateProjectAsync(client, "Event Prep");

        var goal = await CreateGoalAsync(client, RankGoal with
        {
            Projects = [new ProjectPriorityRequest(defaultProject.ProjectId)],
            StartPaused = true,
        });

        var response = await client.PutAsJsonAsync(
            $"/api/v1/me/projects/{projectB.ProjectId}/goals",
            new UpdateProjectGoalsRequest([new ProjectGoalEntryRequest(goal.GoalId)]),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();

        Assert.Equal("Paused", (await GetGoalAsync(client, goal.GoalId)).Status);
    }

    [Fact]
    public async Task RemovingAMembershipDoesNotChangeStatus()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var projectB = await CreateProjectAsync(client, "Event Prep");

        var goal = await CreateGoalAsync(client, RankGoal with
        {
            Projects =
            [
                new ProjectPriorityRequest(defaultProject.ProjectId),
                new ProjectPriorityRequest(projectB.ProjectId),
            ],
        });
        Assert.Equal("Active", goal.Status);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/me/goals/{goal.GoalId}/projects",
            new UpdateGoalProjectsRequest([projectB.ProjectId]),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();

        Assert.Equal("Active", (await GetGoalAsync(client, goal.GoalId)).Status);
    }

    private static async Task<ProjectSummaryResponse> CreateProjectAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/me/projects",
            new CreateProjectRequest(name, null, null),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<ProjectSummaryResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(project);
        return project;
    }

    private static async Task<GoalDetailResponse> CreateGoalAsync(HttpClient client, CreateGoalRequest request)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals", request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var goal = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(goal);
        return goal;
    }

    private static async Task ActivateAsync(HttpClient client, Guid projectId)
    {
        var response = await client.PostAsync(
            $"/api/v1/me/projects/{projectId}/activate", null, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<GoalDetailResponse> GetGoalAsync(HttpClient client, Guid goalId) =>
        (await client.GetFromJsonAsync<GoalDetailResponse>(
            $"/api/v1/me/goals/{goalId}", TestContext.Current.CancellationToken))!;

    private static async Task<ProjectSummaryResponse> GetDefaultProjectAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<ListProjectsResponse>(
            "/api/v1/me/projects", TestContext.Current.CancellationToken);
        Assert.NotNull(response);
        return response.Projects.Single(project => project.IsDefault);
    }
}
