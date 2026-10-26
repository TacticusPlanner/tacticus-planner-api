using System.Net;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>Per-goal <c>notes</c> and request-level <c>position</c> on <c>POST goals</c> (polish-goal-dialogs).</summary>
public sealed class CreateCombinedGoalsNotesAndPositionTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static CombinedGoalSpec Rank(int end, string? notes = null) => new(
        GoalType.Rank,
        new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, end, false, 0)),
        [],
        Notes: notes);

    private static CreateCombinedGoalsRequest Batch(int? position, params CombinedGoalSpec[] goals) =>
        new(GoalEntityType.Character, "blackTerminator", null, [.. goals], Position: position);

    [Fact]
    public async Task NotesAreSavedPerGoal()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var created = await CreateAsync(client, Batch(null, Rank(5, "For Guild Raid"), Rank(6)));

        Assert.Equal("For Guild Raid", created[0].Notes);
        Assert.Null(created[1].Notes);
        var listed = (await client.ListGoalsAsync()).Goals.ToDictionary(goal => goal.GoalId);
        Assert.Equal("For Guild Raid", listed[created[0].GoalId].Notes);
        Assert.Null(listed[created[1].GoalId].Notes);
    }

    [Fact]
    public async Task NotesOverTheLimitAreRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(
            Batch(null, Rank(5), Rank(6, new string('n', GoalValidation.MaxNotesLength + 1))));

        await response.AssertFieldRejectedAsync("notes");
        Assert.Empty((await client.ListGoalsAsync()).Goals);
    }

    [Fact]
    public async Task PositionOneInsertsTheBatchAtTheTopInRequestOrder()
    {
        var (client, existing) = await ThreeGoalsAsync();
        var before = await client.ListGoalsAsync();

        var created = await CreateAsync(client, Batch(1, Rank(10), Rank(11)));

        var after = await client.ListGoalsAsync();
        Assert.Equal(
            created.Select(goal => goal.GoalId).Concat(existing.Select(goal => goal.GoalId)),
            after.Goals.Select(goal => goal.GoalId));
        Assert.Equal([1, 2, 3, 4, 5], after.Goals.Select(goal => goal.GlobalPriority));
        Assert.True(after.OrderRevision > before.OrderRevision);
    }

    [Fact]
    public async Task NullPositionAppends()
    {
        var (client, existing) = await ThreeGoalsAsync();

        var created = await CreateAsync(client, Batch(null, Rank(10), Rank(11)));

        var after = await client.ListGoalsAsync();
        Assert.Equal(
            existing.Select(goal => goal.GoalId).Concat(created.Select(goal => goal.GoalId)),
            after.Goals.Select(goal => goal.GoalId));
        Assert.Equal([1, 2, 3, 4, 5], after.Goals.Select(goal => goal.GlobalPriority));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task OutOfRangePositionIsRejected(int position)
    {
        var (client, _) = await ThreeGoalsAsync();
        var before = await client.ListGoalsAsync();

        var response = await client.PostGoalAsync(Batch(position, Rank(10)));

        await response.AssertFieldRejectedAsync("position");
        var after = await client.ListGoalsAsync();
        Assert.Equal(before.OrderRevision, after.OrderRevision);
        Assert.Equal(before.Goals.Select(goal => goal.GoalId), after.Goals.Select(goal => goal.GoalId));
    }

    private async Task<(HttpClient Client, List<GoalDetailResponse> Goals)> ThreeGoalsAsync()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var goals = new List<GoalDetailResponse>();
        for (var end = 2; end <= 4; end++)
            goals.AddRange(await CreateAsync(client, Batch(null, Rank(end))));
        return (client, goals);
    }

    private static async Task<List<GoalDetailResponse>> CreateAsync(HttpClient client, CreateCombinedGoalsRequest request)
    {
        var response = await client.PostGoalAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateCombinedGoalsResponse>(TestContext.Current.CancellationToken))!.Goals;
    }
}
