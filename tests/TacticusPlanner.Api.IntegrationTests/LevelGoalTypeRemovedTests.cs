using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>The standalone <c>Level</c> goal type no longer exists (integrate-level-progression-into-rank-goals):
/// creating one is rejected, and no goal is persisted.</summary>
public sealed class LevelGoalTypeRemovedTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string CharacterId = "blackTerminator";

    [Theory]
    [InlineData("level")]
    [InlineData("Level")]
    [InlineData("7")] // the removed enum value must not parse from its old numeric form either
    public async Task CreatingALevelGoalIsRejected(string goalType)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostMutatedGoalAsync(
            new CreateGoalRequest(GoalEntityType.Character, CharacterId, GoalType.Unlock, new CreateGoalConfigRequest(), null),
            request => request["goals"]![0]!["goalType"] = goalType);

        await response.AssertFieldRejectedAsync("goalType");
        var goals = await client.GetFromJsonAsync<ListGoalsResponse>("/api/v1/goals", TestContext.Current.CancellationToken);
        Assert.Empty(goals!.Goals);
    }

    [Fact]
    public async Task CreatingACombinedLevelGoalIsRejectedAndNothingIsCreated()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.SendMutatedJsonAsync(
            HttpMethod.Post,
            "/api/v1/goals",
            new CreateCombinedGoalsRequest(
                GoalEntityType.Character,
                CharacterId,
                null,
                [new CombinedGoalSpec(GoalType.Unlock, new CreateGoalConfigRequest(), [])]),
            request => request["goals"]![0]!["goalType"] = "level");

        await response.AssertFieldRejectedAsync("goalType");
        var goals = await client.GetFromJsonAsync<ListGoalsResponse>("/api/v1/goals", TestContext.Current.CancellationToken);
        Assert.Empty(goals!.Goals);
    }
}
