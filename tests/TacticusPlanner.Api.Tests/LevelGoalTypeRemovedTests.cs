using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Goals;

namespace TacticusPlanner.Api.Tests;

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

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            new CreateGoalRequest("character", CharacterId, goalType, new CreateGoalConfigRequest(), null),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var goals = await client.GetFromJsonAsync<ListGoalsResponse>("/api/v1/me/goals", TestContext.Current.CancellationToken);
        Assert.Empty(goals!.Goals);
    }

    [Fact]
    public async Task CreatingACombinedLevelGoalIsRejectedAndNothingIsCreated()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals/combined",
            new CreateCombinedGoalsRequest(
                "character",
                CharacterId,
                null,
                [new CombinedGoalSpec("level", new CreateGoalConfigRequest(), [])]),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var goals = await client.GetFromJsonAsync<ListGoalsResponse>("/api/v1/me/goals", TestContext.Current.CancellationToken);
        Assert.Empty(goals!.Goals);
    }
}
