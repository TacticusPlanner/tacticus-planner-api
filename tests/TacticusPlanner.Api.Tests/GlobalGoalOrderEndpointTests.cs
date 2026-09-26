using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;

namespace TacticusPlanner.Api.Tests;

public sealed class GlobalGoalOrderEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static CreateGoalRequest RankMilestone(int end, params Guid[] projectIds) => new(
        "character",
        "blackTerminator",
        "rank",
        new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, end, false, 0)),
        projectIds.Length == 0 ? null : projectIds.Select(id => new ProjectPriorityRequest(id)).ToList());

    private static readonly CreateGoalRequest MowAbility = new(
        "mow",
        "astraOrdnanceBattery",
        "ability",
        new CreateGoalConfigRequest(Ability: new AbilityTargetRequest(0, 3, 0, 3)),
        null);

    [Fact]
    public async Task GoalsAppendToTheGlobalOrderOnceRegardlessOfProjectCount()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await DefaultProjectAsync(client);
        var extra = await CreateProjectAsync(client, "Extra");

        var shared = await CreateGoalAsync(client, RankMilestone(3, defaultProject.ProjectId, extra.ProjectId));
        var mow = await CreateGoalAsync(client, MowAbility);

        var list = await ListGoalsAsync(client);
        Assert.Equal([shared.GoalId, mow.GoalId], list.Goals.Select(goal => goal.GoalId));
        Assert.Equal([1, 2], list.Goals.Select(goal => goal.GlobalPriority));
        foreach (var projectId in new[] { defaultProject.ProjectId, extra.ProjectId })
        {
            var members = await ProjectMemberIdsAsync(client, projectId);
            Assert.Contains(shared.GoalId, members);
        }
    }

    [Fact]
    public async Task ReorderAppliesAcrossUnitsAndProjectsAndAdvancesTheRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var extra = await CreateProjectAsync(client, "Extra");
        var first = await CreateGoalAsync(client, RankMilestone(3));
        var mow = await CreateGoalAsync(client, MowAbility);
        var third = await CreateGoalAsync(client, RankMilestone(4, extra.ProjectId));
        var before = await ListGoalsAsync(client);

        var response = await ReorderAsync(client, [third.GoalId, first.GoalId, mow.GoalId], before.OrderRevision);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<GoalOrderResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(before.OrderRevision + 1, body.Revision);
        Assert.Equal([third.GoalId, first.GoalId, mow.GoalId], body.GoalIds);
        var after = await ListGoalsAsync(client);
        Assert.Equal(body.GoalIds, after.Goals.Select(goal => goal.GoalId));
        Assert.Equal(body.Revision, after.OrderRevision);
    }

    [Fact]
    public async Task ReorderThatChangesNothingKeepsTheRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var first = await CreateGoalAsync(client, RankMilestone(3));
        var mow = await CreateGoalAsync(client, MowAbility);
        var before = await ListGoalsAsync(client);

        var response = await ReorderAsync(client, [first.GoalId, mow.GoalId], before.OrderRevision);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<GoalOrderResponse>(TestContext.Current.CancellationToken);

        Assert.Equal(before.OrderRevision, body!.Revision);
    }

    [Fact]
    public async Task ReorderRejectsStaleRevisionMissingUnknownAndDuplicateGoalsWithoutChangingAnything()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var first = await CreateGoalAsync(client, RankMilestone(3));
        var mow = await CreateGoalAsync(client, MowAbility);
        var before = await ListGoalsAsync(client);

        var stale = await ReorderAsync(client, [mow.GoalId, first.GoalId], before.OrderRevision + 5);
        await AssertConflictAsync(stale, "goalOrderStale", before);

        var missing = await ReorderAsync(client, [mow.GoalId], before.OrderRevision);
        await AssertConflictAsync(missing, "goalOrderSetMismatch", before);

        var foreign = await ReorderAsync(client, [mow.GoalId, Guid.NewGuid()], before.OrderRevision);
        await AssertConflictAsync(foreign, "goalOrderSetMismatch", before);

        var duplicate = await ReorderAsync(client, [mow.GoalId, mow.GoalId], before.OrderRevision);
        await AssertConflictAsync(duplicate, "goalOrderDuplicate", before);

        var after = await ListGoalsAsync(client);
        Assert.Equal(before.Goals.Select(goal => goal.GoalId), after.Goals.Select(goal => goal.GoalId));
        Assert.Equal(before.OrderRevision, after.OrderRevision);
    }

    [Fact]
    public async Task ReorderRejectsAGoalCreatedAfterTheOrderWasLoaded()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var first = await CreateGoalAsync(client, RankMilestone(3));
        var loaded = await ListGoalsAsync(client);
        _ = await CreateGoalAsync(client, MowAbility);

        var response = await ReorderAsync(client, [first.GoalId], loaded.OrderRevision);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ReorderIsScopedToTheCallersProfile()
    {
        var owner = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ownersGoal = await CreateGoalAsync(owner, RankMilestone(3));
        var other = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var otherGoal = await CreateGoalAsync(other, MowAbility);
        var otherList = await ListGoalsAsync(other);

        var response = await ReorderAsync(other, [otherGoal.GoalId, ownersGoal.GoalId], otherList.OrderRevision);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal([ownersGoal.GoalId], (await ListGoalsAsync(owner)).Goals.Select(goal => goal.GoalId));
    }

    [Fact]
    public async Task ReorderAcceptsADependentAheadOfItsPrerequisite()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var combinedResponse = await client.PostAsJsonAsync(
            "/api/v1/me/goals/combined",
            new CreateCombinedGoalsRequest(
                "character",
                "blackTerminator",
                null,
                [
                    new CombinedGoalSpec("unlock", new CreateGoalConfigRequest(), []),
                    new CombinedGoalSpec("rank",
                        new CreateGoalConfigRequest(Rank: new RankTargetRequest(0, false, 0, 15, false, 0)), [0]),
                ]),
            TestContext.Current.CancellationToken);
        combinedResponse.EnsureSuccessStatusCode();
        var combined = await combinedResponse.Content.ReadFromJsonAsync<CreateCombinedGoalsResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(combined);
        var (unlock, rank) = (combined.Goals[0], combined.Goals[1]);
        var before = await ListGoalsAsync(client);
        Assert.Equal([unlock.GoalId, rank.GoalId], before.Goals.Select(goal => goal.GoalId));

        var response = await ReorderAsync(client, [rank.GoalId, unlock.GoalId], before.OrderRevision);

        response.EnsureSuccessStatusCode();
        var rankDetail = await client.GetFromJsonAsync<GoalDetailResponse>(
            $"/api/v1/me/goals/{rank.GoalId}", TestContext.Current.CancellationToken);
        Assert.Equal([unlock.GoalId], rankDetail!.DependsOn);
    }

    [Fact]
    public async Task LifecycleKeepsTheOrderDenseAndAdvancesTheRevisionOnlyWhenTheSetChanges()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var a = await CreateGoalAsync(client, RankMilestone(3));
        var b = await CreateGoalAsync(client, MowAbility);
        var c = await CreateGoalAsync(client, RankMilestone(4));
        var start = await ListGoalsAsync(client);

        // Pause and resume keep the position and the revision.
        await SetStatusAsync(client, b.GoalId, "paused");
        await SetStatusAsync(client, b.GoalId, "active");
        var afterPause = await ListGoalsAsync(client);
        Assert.Equal(start.OrderRevision, afterPause.OrderRevision);
        Assert.Equal([a.GoalId, b.GoalId, c.GoalId], afterPause.Goals.Select(goal => goal.GoalId));

        // Completing removes the goal from the order and compacts the rest.
        await SetStatusAsync(client, a.GoalId, "completed");
        var afterComplete = await ListGoalsAsync(client);
        Assert.Equal(start.OrderRevision + 1, afterComplete.OrderRevision);
        Assert.Equal([b.GoalId, c.GoalId, a.GoalId], afterComplete.Goals.Select(goal => goal.GoalId));
        Assert.Equal([1, 2, null], afterComplete.Goals.Select(goal => goal.GlobalPriority));

        // Reopening appends after the current in-flight goals.
        await SetStatusAsync(client, a.GoalId, "active");
        var afterReopen = await ListGoalsAsync(client);
        Assert.Equal(start.OrderRevision + 2, afterReopen.OrderRevision);
        Assert.Equal([b.GoalId, c.GoalId, a.GoalId], afterReopen.Goals.Select(goal => goal.GoalId));
        Assert.Equal([1, 2, 3], afterReopen.Goals.Select(goal => goal.GlobalPriority));

        // Deleting an in-flight goal compacts the order.
        var delete = await client.DeleteAsync($"/api/v1/me/goals/{b.GoalId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        var afterDelete = await ListGoalsAsync(client);
        Assert.Equal([c.GoalId, a.GoalId], afterDelete.Goals.Select(goal => goal.GoalId));
        Assert.Equal([1, 2], afterDelete.Goals.Select(goal => goal.GlobalPriority));
        Assert.Equal(afterReopen.OrderRevision + 1, afterDelete.OrderRevision);
    }

    [Fact]
    public async Task MembershipChangesNeverChangeTheOrderOrItsRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var extra = await CreateProjectAsync(client, "Extra");
        var a = await CreateGoalAsync(client, RankMilestone(3));
        var b = await CreateGoalAsync(client, MowAbility);
        var before = await ListGoalsAsync(client);
        var defaultProject = await DefaultProjectAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/me/goals/{a.GoalId}/projects",
            new UpdateGoalProjectsRequest([defaultProject.ProjectId, extra.ProjectId]),
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var members = await client.PutAsJsonAsync(
            $"/api/v1/me/projects/{extra.ProjectId}/goals",
            new UpdateProjectGoalsRequest([new ProjectGoalEntryRequest(a.GoalId), new ProjectGoalEntryRequest(b.GoalId)]),
            TestContext.Current.CancellationToken);
        members.EnsureSuccessStatusCode();

        var after = await ListGoalsAsync(client);
        Assert.Equal(before.OrderRevision, after.OrderRevision);
        Assert.Equal(before.Goals.Select(goal => goal.GoalId), after.Goals.Select(goal => goal.GoalId));
    }

    [Fact]
    public async Task MovingAProjectGoalUpTakesTheDisplacedGoalsGlobalPosition()
    {
        var (client, project, goals) = await FiveGoalsWithProjectHoldingAceAsync();
        var (a, b, c, d, e) = (goals[0], goals[1], goals[2], goals[3], goals[4]);
        var loaded = await ListGoalsAsync(client);

        var response = await MoveAsync(client, project, e, c, loaded.OrderRevision);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<GoalOrderResponse>(TestContext.Current.CancellationToken);
        Assert.Equal([a, b, e, c, d], body!.GoalIds);
        Assert.Equal(loaded.OrderRevision + 1, body.Revision);
        Assert.Equal([a, b, e, c, d], (await ListGoalsAsync(client)).Goals.Select(goal => goal.GoalId));
        Assert.Equal([a, e, c], await ProjectMemberIdsAsync(client, project));
    }

    [Fact]
    public async Task MovingAProjectGoalDownTakesTheDisplacedGoalsGlobalPosition()
    {
        var (client, project, goals) = await FiveGoalsWithProjectHoldingAceAsync();
        var (a, b, c, d, e) = (goals[0], goals[1], goals[2], goals[3], goals[4]);
        var loaded = await ListGoalsAsync(client);

        var response = await MoveAsync(client, project, c, e, loaded.OrderRevision);

        response.EnsureSuccessStatusCode();
        Assert.Equal([a, b, d, e, c], (await ListGoalsAsync(client)).Goals.Select(goal => goal.GoalId));
        Assert.Equal([a, e, c], await ProjectMemberIdsAsync(client, project));
    }

    [Fact]
    public async Task ProjectMoveRejectsNonMembersSelfMovesAndStaleRevisionsWithoutChangingAnything()
    {
        var (client, project, goals) = await FiveGoalsWithProjectHoldingAceAsync();
        var (a, b, c, _, e) = (goals[0], goals[1], goals[2], goals[3], goals[4]);
        var loaded = await ListGoalsAsync(client);

        // B is in the account but not in the project.
        await AssertConflictAsync(await MoveAsync(client, project, e, b, loaded.OrderRevision), "goalOrderSetMismatch", loaded);
        await AssertConflictAsync(await MoveAsync(client, project, b, c, loaded.OrderRevision), "goalOrderSetMismatch", loaded);
        await AssertConflictAsync(await MoveAsync(client, project, a, a, loaded.OrderRevision), "goalOrderSameGoal", loaded);
        await AssertConflictAsync(await MoveAsync(client, project, e, c, loaded.OrderRevision + 1), "goalOrderStale", loaded);
        // A stale client is told so even when its goals also left the project.
        await AssertConflictAsync(await MoveAsync(client, project, b, c, loaded.OrderRevision + 1), "goalOrderStale", loaded);

        var after = await ListGoalsAsync(client);
        Assert.Equal(loaded.Goals.Select(goal => goal.GoalId), after.Goals.Select(goal => goal.GoalId));
        Assert.Equal(loaded.OrderRevision, after.OrderRevision);
    }

    [Fact]
    public async Task ProjectMoveForAnotherProfilesProjectIsNotFound()
    {
        var owner = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var project = await DefaultProjectAsync(owner);
        var other = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await MoveAsync(other, project.ProjectId, Guid.NewGuid(), Guid.NewGuid(), 0);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Five in-flight goals A..E (rank milestones for one unit) in creation order; the project holds A, C and E.
    private async Task<(HttpClient Client, Guid Project, List<Guid> Goals)> FiveGoalsWithProjectHoldingAceAsync()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await DefaultProjectAsync(client);
        var project = await CreateProjectAsync(client, "Subset");
        var goals = new List<Guid>();
        for (var index = 0; index < 5; index++)
        {
            Guid[] projects = index % 2 == 0 ? [project.ProjectId] : [defaultProject.ProjectId];
            goals.Add((await CreateGoalAsync(client, RankMilestone(3 + index, projects))).GoalId);
        }

        return (client, project.ProjectId, goals);
    }

    private static Task<HttpResponseMessage> ReorderAsync(HttpClient client, List<Guid> goalIds, long revision) =>
        client.PutAsJsonAsync(
            "/api/v1/me/goals/order", new UpdateGoalOrderRequest(goalIds, revision), TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> MoveAsync(
        HttpClient client, Guid projectId, Guid goalId, Guid displacedGoalId, long revision) =>
        client.PutAsJsonAsync(
            $"/api/v1/me/projects/{projectId}/goal-order",
            new UpdateProjectGoalOrderRequest(goalId, displacedGoalId, revision),
            TestContext.Current.CancellationToken);

    private static async Task AssertConflictAsync(HttpResponseMessage response, string issueCode, ListGoalsResponse current)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GoalOrderConflictResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(issueCode, body.IssueCode);
        Assert.Equal(current.OrderRevision, body.Revision);
        Assert.Equal(current.Goals.Select(goal => goal.GoalId), body.GoalIds);
    }

    private static async Task SetStatusAsync(HttpClient client, Guid goalId, string status)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{goalId}/status", new UpdateGoalStatusRequest(status), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<ListGoalsResponse> ListGoalsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ListGoalsResponse>("/api/v1/me/goals", TestContext.Current.CancellationToken))!;

    private static async Task<List<Guid>> ProjectMemberIdsAsync(HttpClient client, Guid projectId)
    {
        var members = await client.GetFromJsonAsync<ListProjectGoalsResponse>(
            $"/api/v1/me/projects/{projectId}/goals", TestContext.Current.CancellationToken);
        return members!.Goals.Select(entry => entry.Goal.GoalId).ToList();
    }

    private static async Task<ProjectSummaryResponse> DefaultProjectAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<ListProjectsResponse>(
            "/api/v1/me/projects", TestContext.Current.CancellationToken);
        return response!.Projects.Single(project => project.IsDefault);
    }

    private static async Task<ProjectSummaryResponse> CreateProjectAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/me/projects", new CreateProjectRequest(name, null, null), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectSummaryResponse>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<GoalDetailResponse> CreateGoalAsync(HttpClient client, CreateGoalRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/me/goals", request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken))!;
    }
}
