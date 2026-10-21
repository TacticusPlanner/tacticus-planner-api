using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Goals;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary><c>POST me/goals/status</c> (<c>goal-bulk-status</c>): many goals in one atomic, profile-locked
/// transition.</summary>
public sealed class GoalBulkStatusEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Distinct Rank end targets of one unit are distinct slots.
    private static CreateGoalRequest Rank(int end) => new(
        "character",
        "blackTerminator",
        "rank",
        new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, end, false, 0)),
        null);

    private async Task<(HttpClient Client, GoalDetailResponse A, GoalDetailResponse B, GoalDetailResponse C)> ThreeActiveGoalsAsync()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        return (client, await client.CreateGoalAsync(Rank(3)), await client.CreateGoalAsync(Rank(4)), await client.CreateGoalAsync(Rank(5)));
    }

    [Fact]
    public async Task PausingTwoOfThreeKeepsTheirPositionsAndLeavesTheThirdUnchanged()
    {
        var (client, a, b, c) = await ThreeActiveGoalsAsync();
        var before = await client.ListGoalsAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals/status", new UpdateGoalsStatusRequest([a.GoalId, c.GoalId], "Paused"), Ct);

        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadFromJsonAsync<UpdateGoalsStatusResponse>(Ct))!;
        Assert.Equal([a.GoalId, c.GoalId], body.Goals.Select(goal => goal.GoalId));
        Assert.All(body.Goals, goal => Assert.Equal("Paused", goal.Status));
        Assert.Equal([1, 3], body.Goals.Select(goal => goal.GlobalPriority));
        // Pausing keeps the in-flight set, so the order revision is the current one.
        Assert.Equal(before.OrderRevision, body.OrderRevision);
        var after = await client.ListGoalsAsync();
        Assert.Equal("Active", after.Goals.Single(goal => goal.GoalId == b.GoalId).Status);
        Assert.Equal(b.Revision, after.Goals.Single(goal => goal.GoalId == b.GoalId).Revision);
    }

    [Fact]
    public async Task CompletingOneGoalTakesItOutOfTheOrder()
    {
        var (client, a, b, c) = await ThreeActiveGoalsAsync();
        var before = await client.ListGoalsAsync();

        var response = await client.PostStatusAsync("Completed", before.OrderRevision, a.GoalId);

        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadFromJsonAsync<UpdateGoalsStatusResponse>(Ct))!;
        var completed = Assert.Single(body.Goals);
        Assert.Equal("Completed", completed.Status);
        Assert.Null(completed.GlobalPriority);
        Assert.Equal(before.OrderRevision + 1, body.OrderRevision);
        var after = await client.ListGoalsAsync();
        Assert.Equal([b.GoalId, c.GoalId], after.Goals.Where(goal => goal.GlobalPriority is not null).Select(goal => goal.GoalId));
        Assert.Equal([1, 2], after.Goals.Where(goal => goal.GlobalPriority is not null).Select(goal => goal.GlobalPriority));
    }

    [Fact]
    public async Task AGoalAlreadyInTheStatusIsLeftUntouched()
    {
        var (client, a, _, _) = await ThreeActiveGoalsAsync();
        var paused = await (await client.PostStatusAsync(a.GoalId, "Paused")).ReadStatusGoalAsync();
        var orderRevision = (await client.ListGoalsAsync()).OrderRevision;

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals/status", new UpdateGoalsStatusRequest([a.GoalId], "Paused"), Ct);

        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadFromJsonAsync<UpdateGoalsStatusResponse>(Ct))!;
        Assert.Equal(paused.Revision, Assert.Single(body.Goals).Revision);
        Assert.Equal(orderRevision, body.OrderRevision);
    }

    [Fact]
    public async Task OneConflictingResumeRejectsTheWholeRequest()
    {
        var (client, a, b, _) = await ThreeActiveGoalsAsync();
        (await client.PostStatusAsync("Completed", null, a.GoalId, b.GoalId)).EnsureSuccessStatusCode();
        // Another in-flight goal takes B's slot (Rank 4) while B is Completed.
        var occupant = await client.CreateGoalAsync(Rank(4));

        var response = await client.PostStatusAsync("Active", null, a.GoalId, b.GoalId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadProblemAsync();
        Assert.Equal("slotOccupied", problem["issueCode"]!.GetValue<string>());
        Assert.Equal(b.GoalId, problem["goalId"]!.GetValue<Guid>());
        Assert.Equal(occupant.GoalId, problem["existingGoalId"]!.GetValue<Guid>());
        Assert.Equal(occupant.GoalId, problem["current"]!["goalId"]!.GetValue<Guid>());
        Assert.Equal("Completed", (await client.ListGoalsAsync()).Goals.Single(goal => goal.GoalId == a.GoalId).Status);
    }

    [Fact]
    public async Task AForeignIdAmongOwnGoalsIsNotFoundAndChangesNothing()
    {
        var (client, a, _, _) = await ThreeActiveGoalsAsync();
        var other = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var foreign = await other.CreateGoalAsync(Rank(3));

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals/status", new UpdateGoalsStatusRequest([a.GoalId, foreign.GoalId], "Paused"), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("notFound", await response.ReadIssueCodeAsync());
        Assert.Equal("Active", (await client.ListGoalsAsync()).Goals.Single(goal => goal.GoalId == a.GoalId).Status);
    }

    [Fact]
    public async Task AStaleOrderRevisionIsRejectedWithTheCurrentOrder()
    {
        var (client, a, b, c) = await ThreeActiveGoalsAsync();
        var stale = (await client.ListGoalsAsync()).OrderRevision - 1;

        var response = await client.PostStatusAsync("Completed", stale, a.GoalId, b.GoalId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("goalOrderStale", await response.ReadIssueCodeAsync());
        var current = await response.ReadCurrentAsync<GoalOrderResponse>();
        Assert.Equal(stale + 1, current.Revision);
        Assert.Equal([a.GoalId, b.GoalId, c.GoalId], current.GoalIds);
        Assert.All((await client.ListGoalsAsync()).Goals, goal => Assert.Equal("Active", goal.Status));
    }

    [Fact]
    public async Task LeavingTheOrderRequiresAnExpectedOrderRevision()
    {
        var (client, a, _, _) = await ThreeActiveGoalsAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals/status", new UpdateGoalsStatusRequest([a.GoalId], "Archived"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadProblemAsync();
        Assert.Equal("validation", problem["issueCode"]!.GetValue<string>());
        Assert.NotNull(problem["errors"]!["expectedOrderRevision"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyOrRepeatedGoalIdsAreInvalid(bool repeated)
    {
        var (client, a, _, _) = await ThreeActiveGoalsAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals/status",
            new UpdateGoalsStatusRequest(repeated ? [a.GoalId, a.GoalId] : [], "Paused"),
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull((await response.ReadProblemAsync())["errors"]!["goalIds"]);
    }

    [Fact]
    public async Task AReorderRacingABulkCompletionCommitsFirstOrGoesStale()
    {
        var (client, a, b, c) = await ThreeActiveGoalsAsync();
        var revision = (await client.ListGoalsAsync()).OrderRevision;

        var results = await Task.WhenAll(
            client.PostStatusAsync("Completed", revision, a.GoalId, b.GoalId),
            client.PutAsJsonAsync("/api/v1/me/goals/order", new UpdateGoalOrderRequest([c.GoalId, b.GoalId, a.GoalId], revision), Ct));

        // Serialized by the profile lock: whichever commits second sees a stale revision; never both rejected,
        // never an unhandled error.
        Assert.All(results, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        Assert.Contains(results, response => response.StatusCode == HttpStatusCode.OK);
        if (results[0].StatusCode == HttpStatusCode.Conflict)
        {
            Assert.Equal("goalOrderStale", await results[0].ReadIssueCodeAsync());
            (await client.PostStatusAsync("Completed", null, a.GoalId, b.GoalId)).EnsureSuccessStatusCode();
        }

        var inFlight = (await client.ListGoalsAsync()).Goals.Where(goal => goal.GlobalPriority is not null).ToList();
        Assert.Equal([c.GoalId], inFlight.Select(goal => goal.GoalId));
        Assert.Equal([1], inFlight.Select(goal => goal.GlobalPriority));
    }
}
