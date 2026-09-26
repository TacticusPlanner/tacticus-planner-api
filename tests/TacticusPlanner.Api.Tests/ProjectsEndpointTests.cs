using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;

namespace TacticusPlanner.Api.Tests;

public sealed class ProjectsEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static readonly CreateGoalRequest RankGoal = new(
        "character",
        "blackTerminator",
        "rank",
        new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 5, false, 0)),
        null
    );

    // A second, different-typed goal for the same character — these priority/bulk-status tests need
    // two distinct goal rows in one project, and two Rank goals for the same character would now trip
    // the one-active-or-paused-per-(entity,type) constraint (see GoalsEndpointTests).
    private static readonly CreateGoalRequest AscensionGoal = new(
        "character",
        "blackTerminator",
        "ascension",
        new CreateGoalConfigRequest(Progression: new ProgressionTargetRequest("Common:None", "Common:OneStar")),
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
    public async Task UpdateProjectGoalsKeepsTheGoalsGlobalPosition()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var goal = await CreateGoalAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/me/projects/{defaultProject.ProjectId}/goals",
            new UpdateProjectGoalsRequest([new ProjectGoalEntryRequest(goal.GoalId)]),
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
        var completedGoal = await CreateGoalAsync(client, AscensionGoal);

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
    public async Task ListProjectGoalsReturnsMembersInGlobalPriorityOrder()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var first = await CreateGoalAsync(client);
        var second = await CreateGoalAsync(client, AscensionGoal);

        var order = await client.GetFromJsonAsync<ListGoalsResponse>(
            "/api/v1/me/goals", TestContext.Current.CancellationToken);
        (await client.PutAsJsonAsync(
            "/api/v1/me/goals/order",
            new UpdateGoalOrderRequest([second.GoalId, first.GoalId], order!.OrderRevision),
            TestContext.Current.CancellationToken
        )).EnsureSuccessStatusCode();

        var response = await client.GetAsync(
            $"/api/v1/me/projects/{defaultProject.ProjectId}/goals",
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ListProjectGoalsResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(2, body.Goals.Count);
        Assert.Equal(second.GoalId, body.Goals[0].Goal.GoalId);
        Assert.Equal(1, body.Goals[0].Goal.GlobalPriority);
        Assert.Equal(first.GoalId, body.Goals[1].Goal.GoalId);
        Assert.Equal(2, body.Goals[1].Goal.GlobalPriority);
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

    [Fact]
    public async Task ProjectMembershipReplacementRejectsTwoInFlightGoalsForOneSlot()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var firstProject = await GetDefaultProjectAsync(client);
        var secondProjectResponse = await client.PostAsJsonAsync(
            "/api/v1/me/projects", new CreateProjectRequest("Second", null, null),
            TestContext.Current.CancellationToken);
        var secondProject = await secondProjectResponse.Content.ReadFromJsonAsync<ProjectSummaryResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(secondProject);

        var first = await CreateGoalAsync(client);
        var secondResponse = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal with { Projects = [new ProjectPriorityRequest(secondProject.ProjectId)] },
            TestContext.Current.CancellationToken);
        secondResponse.EnsureSuccessStatusCode();
        var second = await secondResponse.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(second);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/me/projects/{firstProject.ProjectId}/goals",
            new UpdateProjectGoalsRequest([
                new ProjectGoalEntryRequest(first.GoalId),
                new ProjectGoalEntryRequest(second.GoalId),
            ]), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task MembershipReplacementIgnoresSubmittedPriorityForExistingAndNewMembers()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var project = await GetDefaultProjectAsync(client);
        var first = await CreateGoalAsync(client);
        var second = await CreateGoalAsync(client, new CreateGoalRequest(
            "mow", "astraOrdnanceBattery", "ability",
            new CreateGoalConfigRequest(Ability: new AbilityTargetRequest(0, 3, 0, 3)), null));

        var before = await client.GetFromJsonAsync<ListProjectGoalsResponse>(
            $"/api/v1/me/projects/{project.ProjectId}/goals", TestContext.Current.CancellationToken);
        Assert.NotNull(before);
        var firstOriginalPriority = before.Goals.Single(entry => entry.Goal.GoalId == first.GoalId).Goal.GlobalPriority;

        var thirdResponse = await client.PostAsJsonAsync(
            "/api/v1/me/goals", AscensionGoal, TestContext.Current.CancellationToken);
        thirdResponse.EnsureSuccessStatusCode();
        var third = await thirdResponse.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(third);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/me/projects/{project.ProjectId}/goals",
            new UpdateProjectGoalsRequest([
                new ProjectGoalEntryRequest(first.GoalId),
                new ProjectGoalEntryRequest(second.GoalId),
                new ProjectGoalEntryRequest(third.GoalId),
            ]), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<ListProjectGoalsResponse>(
            $"/api/v1/me/projects/{project.ProjectId}/goals", TestContext.Current.CancellationToken);
        Assert.NotNull(after);
        Assert.Equal(firstOriginalPriority, after.Goals.Single(entry => entry.Goal.GoalId == first.GoalId).Goal.GlobalPriority);
        Assert.Equal([first.GoalId, second.GoalId, third.GoalId], after.Goals.Select(entry => entry.Goal.GoalId));
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

    private static async Task<GoalDetailResponse> CreateGoalAsync(
        HttpClient client,
        CreateGoalRequest? request = null
    )
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals", request ?? RankGoal, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var goal = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(goal);
        return goal;
    }
}
