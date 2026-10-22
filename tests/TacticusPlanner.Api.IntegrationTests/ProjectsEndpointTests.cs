using System.Net;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.GameDomain;

namespace TacticusPlanner.Api.IntegrationTests;

public sealed class ProjectsEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static readonly CreateGoalRequest RankGoal = new(
        GoalEntityType.Character,
        "blackTerminator",
        GoalType.Rank,
        new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 5, false, 0)),
        null
    );

    // A second, different-typed goal for the same character — these priority/bulk-status tests need
    // two distinct goal rows in one project, and two Rank goals for the same character would now trip
    // the one-active-or-paused-per-(entity,type) constraint (see GoalsEndpointTests).
    private static readonly CreateGoalRequest AscensionGoal = new(
        GoalEntityType.Character,
        "blackTerminator",
        GoalType.Ascension,
        new CreateGoalConfigRequest(Progression: new ProgressionTargetRequest(UnitProgression.CommonNone, UnitProgression.CommonOneStar)),
        null
    );

    [Fact]
    public async Task ListProjectsProvisionsDefaultProjectOnFirstAccess()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.GetAsync("/api/v1/projects", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ListProjectsResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        var project = Assert.Single(body.Projects);
        Assert.True(project.IsDefault);
        Assert.Equal("My Goals", project.Name);
    }

    [Fact]
    public async Task ListProjectsIsIdempotentDoesNotDuplicateDefaultProject()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        await client.GetAsync("/api/v1/projects", TestContext.Current.CancellationToken);
        var response = await client.GetAsync("/api/v1/projects", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<ListProjectsResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Single(body.Projects);
    }

    [Fact]
    public async Task CreateProjectBlankNameIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/projects",
            new CreateProjectRequest("   ", null, null),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ActivateProjectRouteIsGone()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var projects = await client.GetFromJsonAsync<ListProjectsResponse>(
            "/api/v1/projects", TestContext.Current.CancellationToken);
        // A real project: the removed endpoint answered 200 for it, so only a missing route passes.
        var projectId = Assert.Single(projects!.Projects).ProjectId;

        var response = await client.PostAsync(
            $"/api/v1/projects/{projectId}/activate", null, TestContext.Current.CancellationToken);

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task ProjectSummaryHasNoActivePlanFlag()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var json = await client.GetStringAsync("/api/v1/projects", TestContext.Current.CancellationToken);

        Assert.DoesNotContain("isActivePlan", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateProjectRenamesAndArchivesContainer()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/projects",
            new CreateProjectRequest("Event Prep", null, null),
            TestContext.Current.CancellationToken
        );
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectSummaryResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(project);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/projects/{project.ProjectId}",
            new UpdateProjectRequest("LRE Prep", "Next event", "#6366f1", ProjectStatus.Archived, project.Revision),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<ProjectSummaryResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(updated);
        Assert.Equal("LRE Prep", updated.Name);
        Assert.Equal(ProjectStatus.Archived, updated.Status);
        Assert.True(updated.Revision > project.Revision);
    }

    [Fact]
    public async Task UpdateProjectRejectsStaleRevisionWithIssueCode()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var project = await GetDefaultProjectAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/projects/{project.ProjectId}",
            new UpdateProjectRequest("Changed", null, null, ProjectStatus.Active, project.Revision + 1),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("projectRevisionStale", await response.ReadIssueCodeAsync());
        var current = await response.ReadCurrentAsync<ProjectSummaryResponse>();
        Assert.Equal(project.Revision, current.Revision);
        Assert.Equal(project.Name, current.Name);
    }

    [Fact]
    public async Task DefaultProjectCannotBeArchived()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var project = await GetDefaultProjectAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/projects/{project.ProjectId}",
            new UpdateProjectRequest(project.Name, null, null, ProjectStatus.Archived, project.Revision),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("defaultProjectCannotBeArchived", await response.ReadIssueCodeAsync());
        Assert.Equal(ProjectStatus.Active, (await response.ReadCurrentAsync<ProjectSummaryResponse>()).Status);
    }

    [Fact]
    public async Task UpdateProjectGoalsKeepsTheGoalsGlobalPosition()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var goal = await CreateGoalAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/projects/{defaultProject.ProjectId}/goals",
            new UpdateProjectGoalsRequest([new ProjectGoalEntryRequest(goal.GoalId)], [goal.GoalId]),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ProjectGoalsResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        var entry = Assert.Single(body.Goals);
        Assert.Equal(goal.GoalId, entry.GoalId);
        Assert.Equal(1, entry.GlobalPriority);
    }

    [Fact]
    public async Task UpdateProjectGoalsRemovingGoalsOnlyProjectIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var goal = await CreateGoalAsync(client); // created directly into the default project

        var response = await client.PutAsJsonAsync(
            $"/api/v1/projects/{defaultProject.ProjectId}/goals",
            new UpdateProjectGoalsRequest([], [goal.GoalId]),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _ = goal;
    }

    [Fact]
    public async Task ProjectsListTheirGoalIdsInGlobalOrder()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var first = await CreateGoalAsync(client);
        var second = await CreateGoalAsync(client, AscensionGoal);

        var order = await client.ListGoalsAsync();
        (await client.PutAsJsonAsync(
            "/api/v1/goals/order",
            new UpdateGoalOrderRequest([second.GoalId, first.GoalId], order.OrderRevision),
            TestContext.Current.CancellationToken
        )).EnsureSuccessStatusCode();

        Assert.Equal([second.GoalId, first.GoalId], await client.ProjectGoalIdsAsync(defaultProject.ProjectId));
    }

    [Fact]
    public async Task MembershipReplacementIgnoresSubmittedPriorityForExistingAndNewMembers()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var project = await GetDefaultProjectAsync(client);
        var first = await CreateGoalAsync(client);
        var second = await CreateGoalAsync(client, new CreateGoalRequest(
            GoalEntityType.Mow, "astraOrdnanceBattery", GoalType.Ability,
            new CreateGoalConfigRequest(Ability: new AbilityTargetRequest(0, 3, 0, 3)), null));

        var firstOriginalPriority = (await client.ListGoalsAsync()).Goals.Single(goal => goal.GoalId == first.GoalId).GlobalPriority;

        var third = await CreateGoalAsync(client, AscensionGoal);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/projects/{project.ProjectId}/goals",
            new UpdateProjectGoalsRequest([
                new ProjectGoalEntryRequest(first.GoalId),
                new ProjectGoalEntryRequest(second.GoalId),
                new ProjectGoalEntryRequest(third.GoalId),
            ], [first.GoalId, second.GoalId, third.GoalId]), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        Assert.Equal(firstOriginalPriority, (await client.ListGoalsAsync()).Goals.Single(goal => goal.GoalId == first.GoalId).GlobalPriority);
        Assert.Equal([first.GoalId, second.GoalId, third.GoalId], await client.ProjectGoalIdsAsync(project.ProjectId));
    }

    private static async Task<ProjectSummaryResponse> GetDefaultProjectAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<ListProjectsResponse>(
            "/api/v1/projects",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(response);
        return response.Projects.Single(project => project.IsDefault);
    }

    private static Task<GoalDetailResponse> CreateGoalAsync(HttpClient client, CreateGoalRequest? request = null) =>
        client.CreateGoalAsync(request ?? RankGoal);
}
