using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>
/// Regression guard for goal-lifecycle-status: "Project membership operations do not change a goal's
/// status". Membership changes (through either direction of the membership endpoints) may not move a goal
/// between Active and Paused — a goal's status changes only through <c>POST goals/status</c>, which names the
/// goals it transitions (covered by <see cref="GoalBulkStatusEndpointTests"/>).
/// </summary>
public sealed class GoalStatusInvarianceTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static readonly CreateGoalRequest RankGoal = new(
        GoalEntityType.Character,
        "blackTerminator",
        GoalType.Rank,
        new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 5, false, 0)),
        null
    );

    [Fact]
    public async Task AddingAMembershipThroughTheGoalDoesNotChangeStatus()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var projectB = await CreateProjectAsync(client, "Event Prep");

        var goal = await client.CreateGoalAsync(RankGoal with
        {
            Projects = [new ProjectPriorityRequest(defaultProject.ProjectId)],
            StartPaused = true,
        });
        Assert.Equal(GoalStatus.Paused, goal.Status);

        var response = await client.PutProjectsAsync(goal.GoalId, [defaultProject.ProjectId, projectB.ProjectId]);
        response.EnsureSuccessStatusCode();

        Assert.Equal(GoalStatus.Paused, (await GetGoalAsync(client, goal.GoalId)).Status);
    }

    [Fact]
    public async Task AddingAMembershipThroughTheProjectDoesNotChangeStatus()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var projectB = await CreateProjectAsync(client, "Event Prep");

        var goal = await client.CreateGoalAsync(RankGoal with
        {
            Projects = [new ProjectPriorityRequest(defaultProject.ProjectId)],
            StartPaused = true,
        });

        var response = await client.PutAsJsonAsync(
            $"/api/v1/projects/{projectB.ProjectId}/goals",
            new UpdateProjectGoalsRequest([new ProjectGoalEntryRequest(goal.GoalId)], []),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();

        Assert.Equal(GoalStatus.Paused, (await GetGoalAsync(client, goal.GoalId)).Status);
    }

    [Fact]
    public async Task RemovingAMembershipDoesNotChangeStatus()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var projectB = await CreateProjectAsync(client, "Event Prep");

        var goal = await client.CreateGoalAsync(RankGoal with
        {
            Projects =
            [
                new ProjectPriorityRequest(defaultProject.ProjectId),
                new ProjectPriorityRequest(projectB.ProjectId),
            ],
        });
        Assert.Equal(GoalStatus.Active, goal.Status);

        var response = await client.PutProjectsAsync(goal.GoalId, [projectB.ProjectId]);
        response.EnsureSuccessStatusCode();

        Assert.Equal(GoalStatus.Active, (await GetGoalAsync(client, goal.GoalId)).Status);
    }

    private static async Task<ProjectSummaryResponse> CreateProjectAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/projects",
            new CreateProjectRequest(name, null, null),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<ProjectSummaryResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(project);
        return project;
    }

    private static async Task<GoalDetailResponse> GetGoalAsync(HttpClient client, Guid goalId) =>
        (await client.GetFromJsonAsync<GoalDetailResponse>(
            $"/api/v1/goals/{goalId}", TestContext.Current.CancellationToken))!;

    private static async Task<ProjectSummaryResponse> GetDefaultProjectAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<ListProjectsResponse>(
            "/api/v1/projects", TestContext.Current.CancellationToken);
        Assert.NotNull(response);
        return response.Projects.Single(project => project.IsDefault);
    }
}
