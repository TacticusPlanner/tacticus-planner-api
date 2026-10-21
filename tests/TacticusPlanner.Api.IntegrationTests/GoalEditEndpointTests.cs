using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>Coverage for <c>goal-combined-edit</c>: <c>PUT /me/goals/{id}</c>, the only goal edit route, applies
/// target, details, projects and priority in one all-or-nothing request and answers <c>{ goal, order }</c>.
/// PostgreSQL HTTP tests cover mapping, validation and rollback semantics (nothing is saved unless every section
/// applied); the lock behavior is covered by the Postgres integration tests.</summary>
public sealed class GoalEditEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static CreateGoalRequest RankMilestone(int end, params Guid[] projectIds) => new(
        "character",
        "blackTerminator",
        "rank",
        new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, end, false, 0)),
        projectIds.Length == 0 ? null : projectIds.Select(id => new ProjectPriorityRequest(id)).ToList());

    private static UpdateGoalTargetRequest RankEdit(long revision, int end) =>
        new(revision, new GoalTargetEditRequest(Rank: new RankEndTargetRequest(end, false, 0)));

    private static UpdateGoalRequest Notes(string? notes) => new(notes, null);

    [Fact]
    public async Task AllFourSectionsApplyTogetherWithASingleRevisionBump()
    {
        var (client, goals) = await FiveGoalsAsync();
        var extra = await CreateProjectAsync(client, "Extra");
        var project = await DefaultProjectAsync(client);
        var goal = goals[4];
        var loaded = await GetGoalAsync(client, goal.GoalId);
        var order = await ListGoalsAsync(client);

        var response = await EditAsync(client, goal.GoalId, new EditGoalRequest(
            Target: RankEdit(loaded.Revision, 20),
            Details: Notes("all at once"),
            ProjectIds: [project.ProjectId, extra.ProjectId],
            Priority: new EditGoalPriorityRequest(1, order.OrderRevision)));

        response.EnsureSuccessStatusCode();
        var body = await ReadAsync<EditGoalResponse>(response);
        Assert.Equal(20, body.Goal.Config.Rank!.End);
        Assert.Equal("all at once", body.Goal.Notes);
        Assert.Equal(loaded.Revision + 1, body.Goal.Revision);
        Assert.Equal(1, body.Goal.GlobalPriority);
        Assert.Equal([project.ProjectId, extra.ProjectId], body.Goal.ProjectIds.Order());
        Assert.NotNull(body.Order);
        Assert.Equal(order.OrderRevision + 1, body.Order.Revision);
        Assert.Equal(goal.GoalId, body.Order.GoalIds[0]);

        var after = await ListGoalsAsync(client);
        Assert.Equal(body.Order.GoalIds, after.Goals.Select(entry => entry.GoalId));
        Assert.Equal(body.Order.Revision, after.OrderRevision);
        var stored = await GetGoalAsync(client, goal.GoalId);
        Assert.Equal(20, stored.Config.Rank!.End);
        Assert.Equal("all at once", stored.Notes);
        Assert.Equal(loaded.Revision + 1, stored.Revision);
    }

    [Fact]
    public async Task EachSectionAloneLeavesTheOthersUntouched()
    {
        var (client, goals) = await FiveGoalsAsync();
        var extra = await CreateProjectAsync(client, "Extra");
        var goal = goals[2];
        var start = await GetGoalAsync(client, goal.GoalId);
        var startOrder = await ListGoalsAsync(client);

        // Details only.
        var details = await ReadAsync<EditGoalResponse>(
            await EditAsync(client, goal.GoalId, new EditGoalRequest(Details: Notes("only notes"))));
        Assert.Equal("only notes", details.Goal.Notes);
        Assert.Equal(start.Config.Rank!.End, details.Goal.Config.Rank!.End);
        Assert.Equal(start.ProjectIds, details.Goal.ProjectIds);
        Assert.Null(details.Order);

        // Target only: notes stay.
        var target = await ReadAsync<EditGoalResponse>(
            await EditAsync(client, goal.GoalId, new EditGoalRequest(Target: RankEdit(details.Goal.Revision, 9))));
        Assert.Equal(9, target.Goal.Config.Rank!.End);
        Assert.Equal("only notes", target.Goal.Notes);

        // Projects only.
        var projects = await ReadAsync<EditGoalResponse>(
            await EditAsync(client, goal.GoalId, new EditGoalRequest(ProjectIds: [extra.ProjectId])));
        Assert.Equal([extra.ProjectId], projects.Goal.ProjectIds);
        Assert.Equal(9, projects.Goal.Config.Rank!.End);

        // Priority only: order changes, target/notes/projects stay.
        var priority = await ReadAsync<EditGoalResponse>(
            await EditAsync(client, goal.GoalId, new EditGoalRequest(
                Priority: new EditGoalPriorityRequest(5, startOrder.OrderRevision))));
        Assert.Equal(5, priority.Goal.GlobalPriority);
        Assert.Equal(9, priority.Goal.Config.Rank!.End);
        Assert.Equal([extra.ProjectId], priority.Goal.ProjectIds);
        Assert.Equal(startOrder.OrderRevision + 1, priority.Order!.Revision);
    }

    [Fact]
    public async Task EmptyRequestIsANoOp()
    {
        var (client, goals) = await FiveGoalsAsync();
        var before = await GetGoalAsync(client, goals[0].GoalId);
        var orderBefore = await ListGoalsAsync(client);

        var response = await EditAsync(client, goals[0].GoalId, new EditGoalRequest());

        response.EnsureSuccessStatusCode();
        var body = await ReadAsync<EditGoalResponse>(response);
        Assert.Equal(before.Revision, body.Goal.Revision);
        Assert.Null(body.Order);
        Assert.Equal(orderBefore.OrderRevision, (await ListGoalsAsync(client)).OrderRevision);
    }

    [Fact]
    public async Task UnknownAndForeignGoalsAre404()
    {
        var (client, goals) = await FiveGoalsAsync();
        var other = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await EditAsync(client, Guid.NewGuid(), new EditGoalRequest())).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await EditAsync(other, goals[0].GoalId, new EditGoalRequest(Details: Notes("mine now")))).StatusCode);
        Assert.Null((await GetGoalAsync(client, goals[0].GoalId)).Notes);
    }

    [Fact]
    public async Task InvalidTargetRejectsTheWholeRequestAndNamesTheTarget()
    {
        var (client, goals) = await FiveGoalsAsync();
        var goal = await GetGoalAsync(client, goals[0].GoalId);

        // An ascension payload on a Rank goal is invalid.
        var invalid = new UpdateGoalTargetRequest(
            goal.Revision, new GoalTargetEditRequest(Progression: new ProgressionEndTargetRequest("Common:OneStar")));
        var response = await EditAsync(client, goal.GoalId, new EditGoalRequest(Target: invalid, Details: Notes("lost")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("target", await ErrorKeysAsync(response), StringComparer.OrdinalIgnoreCase);
        Assert.Null((await GetGoalAsync(client, goal.GoalId)).Notes);
    }

    [Fact]
    public async Task InvalidDetailsNameTheSectionField()
    {
        var (client, goals) = await FiveGoalsAsync();

        var response = await EditAsync(client, goals[0].GoalId, new EditGoalRequest(
            Details: new UpdateGoalRequest("x", null, "NotAStrategy")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation", await response.ReadIssueCodeAsync());
        Assert.Contains("details.farmingStrategy", await ErrorKeysAsync(response));
        Assert.Null((await GetGoalAsync(client, goals[0].GoalId)).Notes);
    }

    [Fact]
    public async Task EmptyOrUnknownProjectListsAreRejected()
    {
        var (client, goals) = await FiveGoalsAsync();

        var empty = await EditAsync(client, goals[0].GoalId, new EditGoalRequest(ProjectIds: []));
        var unknown = await EditAsync(client, goals[0].GoalId, new EditGoalRequest(ProjectIds: [Guid.NewGuid()]));

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task SlotCheckSeesTheNewTargetAndNothingIsSaved()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var projectA = await DefaultProjectAsync(client);
        var projectB = await CreateProjectAsync(client, "Second plan");
        var goal = await CreateGoalAsync(client, RankMilestone(5, projectA.ProjectId));
        var occupant = await CreateGoalAsync(client, RankMilestone(7, projectB.ProjectId));

        var response = await EditAsync(client, goal.GoalId, new EditGoalRequest(
            Target: RankEdit(goal.Revision, 7),
            Details: Notes("lost"),
            ProjectIds: [projectA.ProjectId, projectB.ProjectId]));

        // The occupant is in another project: slots are per profile.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var conflict = await response.ReadProblemAsync();
        Assert.Equal("slotOccupied", conflict["issueCode"]!.GetValue<string>());
        Assert.Equal(occupant.GoalId, conflict["existingGoalId"]!.GetValue<Guid>());
        Assert.Equal(goal.GoalId, conflict["goalId"]!.GetValue<Guid>());
        Assert.Equal("7:0", conflict["normalizedTarget"]!.GetValue<string>());
        Assert.Equal(occupant.GoalId, (await response.ReadCurrentAsync<GoalDetailResponse>()).GoalId);
        var stored = await GetGoalAsync(client, goal.GoalId);
        Assert.Equal(5, stored.Config.Rank!.End);
        Assert.Null(stored.Notes);
        Assert.Equal([projectA.ProjectId], stored.ProjectIds);
        Assert.Equal(goal.Revision, stored.Revision);
    }

    [Fact]
    public async Task StaleGoalRevisionIs409WithTheCurrentGoalAndValidNotesAreNotSaved()
    {
        var (client, goals) = await FiveGoalsAsync();
        var goal = await GetGoalAsync(client, goals[0].GoalId);
        (await client.PutTargetAsync(goal.GoalId, RankEdit(goal.Revision, 8))).EnsureSuccessStatusCode();

        var response = await EditAsync(client, goal.GoalId, new EditGoalRequest(
            Target: RankEdit(goal.Revision, 9), Details: Notes("lost")));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("goalRevisionStale", await response.ReadIssueCodeAsync());
        var current = await response.ReadCurrentAsync<GoalDetailResponse>();
        Assert.Equal(8, current.Config.Rank!.End);
        // current is the goal's read shape.
        Assert.Equivalent(await GetGoalAsync(client, goal.GoalId), current, strict: true);
        var stored = await GetGoalAsync(client, goal.GoalId);
        Assert.Null(stored.Notes);
        Assert.Equal(8, stored.Config.Rank!.End);
    }

    [Fact]
    public async Task StaleOrderRevisionIs409WithTheCurrentOrderAndTheValidTargetIsNotSaved()
    {
        var (client, goals) = await FiveGoalsAsync();
        var goal = await GetGoalAsync(client, goals[0].GoalId);
        var order = await ListGoalsAsync(client);

        var response = await EditAsync(client, goal.GoalId, new EditGoalRequest(
            Target: RankEdit(goal.Revision, 15),
            Priority: new EditGoalPriorityRequest(3, order.OrderRevision + 4)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("goalOrderStale", await response.ReadIssueCodeAsync());
        var current = await response.ReadCurrentAsync<GoalOrderResponse>();
        Assert.Equal(order.OrderRevision, current.Revision);
        Assert.Equal(order.Goals.Select(entry => entry.GoalId), current.GoalIds);
        Assert.Equal(goal.Config.Rank!.End, (await GetGoalAsync(client, goal.GoalId)).Config.Rank!.End);
        Assert.Equal(goal.Revision, (await GetGoalAsync(client, goal.GoalId)).Revision);
    }

    [Fact]
    public async Task PositionOutOfRangeIs400AndAFailingLastSectionLeavesEarlierSectionsUnwritten()
    {
        var (client, goals) = await FiveGoalsAsync();
        var extra = await CreateProjectAsync(client, "Extra");
        var goal = await GetGoalAsync(client, goals[0].GoalId);
        var order = await ListGoalsAsync(client);

        foreach (var position in new[] { 6, 0, -1 })
        {
            var response = await EditAsync(client, goal.GoalId, new EditGoalRequest(
                Target: RankEdit(goal.Revision, 15),
                Details: Notes("lost"),
                ProjectIds: [extra.ProjectId],
                Priority: new EditGoalPriorityRequest(position, order.OrderRevision)));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(await ErrorKeysAsync(response), key => key.StartsWith("priority", StringComparison.OrdinalIgnoreCase));
        }

        var stored = await GetGoalAsync(client, goal.GoalId);
        Assert.Equal(goal.Config.Rank!.End, stored.Config.Rank!.End);
        Assert.Null(stored.Notes);
        Assert.Equal(goal.ProjectIds, stored.ProjectIds);
        Assert.Equal(goal.Revision, stored.Revision);
        var after = await ListGoalsAsync(client);
        Assert.Equal(order.OrderRevision, after.OrderRevision);
        Assert.Equal(order.Goals.Select(entry => entry.GoalId), after.Goals.Select(entry => entry.GoalId));
    }

    [Fact]
    public async Task PriorityOnAGoalWithoutAPositionIs400()
    {
        var (client, goals) = await FiveGoalsAsync();
        var order = await ListGoalsAsync(client);
        (await client.PostStatusAsync(goals[0].GoalId, "completed")).EnsureSuccessStatusCode();
        order = await ListGoalsAsync(client);

        var response = await EditAsync(client, goals[0].GoalId, new EditGoalRequest(
            Details: Notes("lost"), Priority: new EditGoalPriorityRequest(1, order.OrderRevision)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await GetGoalAsync(client, goals[0].GoalId)).Notes);
    }

    [Fact]
    public async Task MoveByPositionShiftsTheGoalsBetweenTheOldAndNewPositions()
    {
        // A, B, C, D, E: E to position 3 gives A, B, E, C, D.
        var (client, goals) = await FiveGoalsAsync();
        var ids = goals.Select(goal => goal.GoalId).ToList();
        var order = await ListGoalsAsync(client);
        var up = await ReadAsync<EditGoalResponse>(await EditAsync(client, ids[4], new EditGoalRequest(
            Priority: new EditGoalPriorityRequest(3, order.OrderRevision))));
        Assert.Equal([ids[0], ids[1], ids[4], ids[2], ids[3]], up.Order!.GoalIds);
        Assert.Equal(order.OrderRevision + 1, up.Order.Revision);
        Assert.Equal([1, 2, 3, 4, 5], (await ListGoalsAsync(client)).Goals.Select(goal => goal.GlobalPriority));
    }

    [Fact]
    public async Task MoveDownAndUnchangedPositions()
    {
        // A, B, C, D, E: C to position 5 gives A, B, D, E, C.
        var (client, goals) = await FiveGoalsAsync();
        var ids = goals.Select(goal => goal.GoalId).ToList();
        var order = await ListGoalsAsync(client);
        var down = await ReadAsync<EditGoalResponse>(await EditAsync(client, ids[2], new EditGoalRequest(
            Priority: new EditGoalPriorityRequest(5, order.OrderRevision))));
        Assert.Equal([ids[0], ids[1], ids[3], ids[4], ids[2]], down.Order!.GoalIds);

        // Same position: 200, revision unchanged, goal revision unchanged.
        var goalBefore = await GetGoalAsync(client, ids[2]);
        var same = await ReadAsync<EditGoalResponse>(await EditAsync(client, ids[2], new EditGoalRequest(
            Priority: new EditGoalPriorityRequest(5, down.Order.Revision))));
        Assert.Equal(down.Order.Revision, same.Order!.Revision);
        Assert.Equal(down.Order.GoalIds, same.Order.GoalIds);
        Assert.Equal(goalBefore.Revision, same.Goal.Revision);
    }

    [Fact]
    public async Task UnchangedTargetInACombinedEditIsANoOpNotAnError()
    {
        var (client, goals) = await FiveGoalsAsync();
        var goal = await GetGoalAsync(client, goals[0].GoalId);

        var response = await EditAsync(client, goal.GoalId, new EditGoalRequest(
            Target: RankEdit(goal.Revision, goal.Config.Rank!.End), Details: Notes("kept")));

        response.EnsureSuccessStatusCode();
        var body = await ReadAsync<EditGoalResponse>(response);
        Assert.Equal("kept", body.Goal.Notes);
        Assert.Equal(goal.Config.Rank.End, body.Goal.Config.Rank!.End);
        // Only the notes changed: one revision step.
        Assert.Equal(goal.Revision + 1, body.Goal.Revision);
    }

    private async Task<(HttpClient Client, List<GoalDetailResponse> Goals)> FiveGoalsAsync()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var goals = new List<GoalDetailResponse>();
        for (var index = 0; index < 5; index++)
            goals.Add(await CreateGoalAsync(client, RankMilestone(2 + index)));
        return (client, goals);
    }

    private static Task<HttpResponseMessage> EditAsync(HttpClient client, Guid goalId, EditGoalRequest request) =>
        client.EditGoalAsync(goalId, request);

    private static async Task<List<string>> ErrorKeysAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name).ToList();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        return body;
    }

    private static Task<GoalDetailResponse> CreateGoalAsync(HttpClient client, CreateGoalRequest request) =>
        client.CreateGoalAsync(request);

    private static async Task<GoalDetailResponse> GetGoalAsync(HttpClient client, Guid goalId) =>
        (await client.GetFromJsonAsync<GoalDetailResponse>($"/api/v1/me/goals/{goalId}", TestContext.Current.CancellationToken))!;

    private static async Task<ListGoalsResponse> ListGoalsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ListGoalsResponse>("/api/v1/me/goals", TestContext.Current.CancellationToken))!;

    private static async Task<ProjectSummaryResponse> CreateProjectAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/me/projects", new CreateProjectRequest(name, null, null), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadAsync<ProjectSummaryResponse>(response);
    }

    private static async Task<ProjectSummaryResponse> DefaultProjectAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<ListProjectsResponse>(
            "/api/v1/me/projects", TestContext.Current.CancellationToken);
        return response!.Projects.Single(project => project.IsDefault);
    }
}
