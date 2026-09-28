using System.Net;
using System.Net.Http.Json;
using System.Text;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;

namespace TacticusPlanner.Api.Tests;

public sealed class ProjectMembershipReplacementTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    // Distinct Rank targets for one unit occupy distinct slots, so any number can share a project.
    private static CreateGoalRequest RankMilestone(int end) => new(
        "character",
        "blackTerminator",
        "rank",
        new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, end, false, 0)),
        null);

    [Fact]
    public async Task FreshReplacementCommitsAndKeepsEveryGlobalPositionAndTheOrderRevision()
    {
        var (client, extra, goals) = await SevenGoalsWithExtraHoldingAsync(0, 1, 6);
        var before = await ListGoalsAsync(client);

        // Add the goal at global position 4 to a project holding positions 1, 2 and 7.
        var add = await ReplaceAsync(client, extra, [goals[0], goals[1], goals[3], goals[6]], [goals[0], goals[1], goals[6]]);
        add.EnsureSuccessStatusCode();
        var added = await add.Content.ReadFromJsonAsync<ProjectGoalsResponse>(TestContext.Current.CancellationToken);
        Assert.Equal([1, 2, 4, 7], added!.Goals.Select(entry => entry.GlobalPriority));
        Assert.Equal([goals[0], goals[1], goals[3], goals[6]], added.Goals.Select(entry => entry.GoalId));

        // Add another goal and remove one in the same save.
        var swap = await ReplaceAsync(client, extra, [goals[0], goals[3], goals[4], goals[6]], [goals[0], goals[1], goals[3], goals[6]]);
        swap.EnsureSuccessStatusCode();

        var after = await ListGoalsAsync(client);
        Assert.Equal(before.OrderRevision, after.OrderRevision);
        Assert.Equal(before.Goals.Select(goal => (goal.GoalId, goal.GlobalPriority)),
            after.Goals.Select(goal => (goal.GoalId, goal.GlobalPriority)));
    }

    [Fact]
    public async Task StaleReviewedSetIsRejectedAtomicallyWithTheCurrentIds()
    {
        var (client, extra, goals) = await SevenGoalsWithExtraHoldingAsync(0);
        // Another edit wins first.
        (await ReplaceAsync(client, extra, [goals[0], goals[1]], [goals[0]])).EnsureSuccessStatusCode();

        var stale = await ReplaceAsync(client, extra, [goals[0], goals[2]], [goals[0]]);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var body = await stale.Content.ReadFromJsonAsync<ProjectMembershipStaleResponse>(TestContext.Current.CancellationToken);
        Assert.Equal("projectMembershipStale", body!.IssueCode);
        Assert.Equal(extra, body.ProjectId);
        Assert.Equal(new HashSet<Guid> { goals[0], goals[1] }, body.CurrentGoalIds.ToHashSet());
        Assert.Equal([goals[0], goals[1]], await ProjectMemberIdsAsync(client, extra));
    }

    [Fact]
    public async Task ConcurrentReorderDoesNotMakeAMembershipSaveStale()
    {
        var (client, extra, goals) = await SevenGoalsWithExtraHoldingAsync(0);
        var loaded = await ListGoalsAsync(client);
        var reversed = loaded.Goals.Select(goal => goal.GoalId).Reverse().ToList();
        (await client.PutAsJsonAsync(
            "/api/v1/me/goals/order", new UpdateGoalOrderRequest(reversed, loaded.OrderRevision),
            TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var reordered = await ListGoalsAsync(client);

        var response = await ReplaceAsync(client, extra, [goals[0], goals[1]], [goals[0]]);

        response.EnsureSuccessStatusCode();
        var after = await ListGoalsAsync(client);
        Assert.Equal(reordered.OrderRevision, after.OrderRevision);
        Assert.Equal(reversed, after.Goals.Select(goal => goal.GoalId));
    }

    [Fact]
    public async Task UnknownGoalRejectsTheWholeBatch()
    {
        var (client, extra, goals) = await SevenGoalsWithExtraHoldingAsync(0);

        var response = await ReplaceAsync(client, extra, [goals[0], goals[1], Guid.NewGuid()], [goals[0]]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([goals[0]], await ProjectMemberIdsAsync(client, extra));
    }

    [Fact]
    public async Task RemovingAGoalsOnlyProjectBlocksTheWholeBatchAndNamesTheGoal()
    {
        var (client, extra, goals) = await SevenGoalsWithExtraHoldingAsync(0);
        // A goal that lives only in the extra project.
        var solo = await client.PostAsJsonAsync(
            "/api/v1/me/goals", RankMilestone(9) with { Projects = [new ProjectPriorityRequest(extra)] },
            TestContext.Current.CancellationToken);
        solo.EnsureSuccessStatusCode();
        var soloId = (await solo.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken))!.GoalId;

        // Adds two valid goals but removes the solo goal from its only project.
        var response = await ReplaceAsync(client, extra, [goals[0], goals[1], goals[2]], [goals[0], soloId]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ProjectLastMembershipResponse>(TestContext.Current.CancellationToken);
        Assert.Equal("lastProjectMembership", body!.IssueCode);
        Assert.Equal([soloId], body.BlockedGoalIds);
        Assert.Equal(new HashSet<Guid> { goals[0], soloId }, (await ProjectMemberIdsAsync(client, extra)).ToHashSet());
    }

    [Fact]
    public async Task OccupiedSlotRejectsTheWholeBatchWithTheStructuredSlotConflict()
    {
        var (client, extra, goals) = await SevenGoalsWithExtraHoldingAsync(0);
        // A second in-flight goal for goals[0]'s slot, living only in a third project.
        var third = await client.PostAsJsonAsync(
            "/api/v1/me/projects", new CreateProjectRequest("Third", null, null), TestContext.Current.CancellationToken);
        third.EnsureSuccessStatusCode();
        var thirdId = (await third.Content.ReadFromJsonAsync<ProjectSummaryResponse>(TestContext.Current.CancellationToken))!.ProjectId;
        var duplicate = await client.PostAsJsonAsync(
            "/api/v1/me/goals", RankMilestone(2) with { Projects = [new ProjectPriorityRequest(thirdId)] },
            TestContext.Current.CancellationToken);
        duplicate.EnsureSuccessStatusCode();
        var duplicateId = (await duplicate.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken))!.GoalId;

        var response = await ReplaceAsync(client, extra, [goals[0], duplicateId, goals[3]], [goals[0]]);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ProjectGoalSlotConflictResponse>(TestContext.Current.CancellationToken);
        Assert.Equal("projectGoalSlotOccupied", body!.IssueCode);
        Assert.Equal([goals[0]], await ProjectMemberIdsAsync(client, extra));
    }

    [Fact]
    public async Task MissingExpectedGoalIdsIsRejected()
    {
        var (client, extra, goals) = await SevenGoalsWithExtraHoldingAsync(0);
        using var content = new StringContent(
            $"{{\"goals\":[{{\"goalId\":\"{goals[0]}\"}}]}}", Encoding.UTF8, "application/json");

        var response = await client.PutAsync(
            $"/api/v1/me/projects/{extra}/goals", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static Task<HttpResponseMessage> ReplaceAsync(
        HttpClient client, Guid projectId, List<Guid> desired, List<Guid> expected) =>
        client.PutAsJsonAsync(
            $"/api/v1/me/projects/{projectId}/goals",
            new UpdateProjectGoalsRequest(desired.Select(id => new ProjectGoalEntryRequest(id)).ToList(), expected),
            TestContext.Current.CancellationToken);

    // Seven goals (global positions 1..7) all in the default project, plus an "Extra" project holding the
    // goals at the given zero-based indexes.
    private async Task<(HttpClient Client, Guid Extra, List<Guid> Goals)> SevenGoalsWithExtraHoldingAsync(
        params int[] extraIndexes)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await client.PostAsJsonAsync(
            "/api/v1/me/projects", new CreateProjectRequest("Extra", null, null), TestContext.Current.CancellationToken);
        created.EnsureSuccessStatusCode();
        var extra = (await created.Content.ReadFromJsonAsync<ProjectSummaryResponse>(TestContext.Current.CancellationToken))!.ProjectId;

        var goals = new List<Guid>();
        for (var end = 2; end <= 8; end++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/me/goals", RankMilestone(end), TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();
            goals.Add((await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken))!.GoalId);
        }

        var members = extraIndexes.Select(index => goals[index]).ToList();
        (await ReplaceAsync(client, extra, members, [])).EnsureSuccessStatusCode();
        return (client, extra, goals);
    }

    private static async Task<ListGoalsResponse> ListGoalsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ListGoalsResponse>("/api/v1/me/goals", TestContext.Current.CancellationToken))!;

    private static async Task<List<Guid>> ProjectMemberIdsAsync(HttpClient client, Guid projectId)
    {
        var members = await client.GetFromJsonAsync<ListProjectGoalsResponse>(
            $"/api/v1/me/projects/{projectId}/goals", TestContext.Current.CancellationToken);
        return members!.Goals.Select(entry => entry.Goal.GoalId).ToList();
    }
}
