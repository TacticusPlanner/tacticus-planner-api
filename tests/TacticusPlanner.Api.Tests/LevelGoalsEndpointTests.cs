using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Goals;

namespace TacticusPlanner.Api.Tests;

/// <summary>Coverage for the <c>Level</c> goal type — a target character level, uncosted (no
/// resource estimate — no XP-cost curve exists yet). Character-only (a Mow's own <c>xpLevel</c> is
/// never targeted). Mirrors <see cref="UpgradeAndEquipmentGoalsEndpointTests"/>'s own coverage
/// style for a similarly simple, un-costed target.</summary>
public sealed class LevelGoalsEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    // A real served catalog character (units-blacklegion.json) — see UpgradeAndEquipmentGoalsEndpointTests.
    private const string CharacterId = "blackTerminator";

    private static readonly CreateGoalRequest LevelGoal = new(
        "character",
        CharacterId,
        "level",
        new CreateGoalConfigRequest(Level: new LevelTargetRequest(1, 10)),
        null
    );

    [Fact]
    public async Task CreateLevelGoalWithValidRangeIsAccepted()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals", LevelGoal, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(created);
        Assert.Equal("Character", created.EntityType);
        Assert.Equal("Level", created.GoalType);
        Assert.Equal(1, created.Config.Level!.Start);
        Assert.Equal(10, created.Config.Level!.End);
    }

    [Theory]
    [InlineData(10, 10)] // target not above the (effective) starting level
    [InlineData(10, 5)] // inverted range
    [InlineData(1, 61)] // above the level cap
    public async Task CreateLevelGoalWithInvalidRangeIsRejected(int start, int end)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            LevelGoal with { Config = new CreateGoalConfigRequest(Level: new LevelTargetRequest(start, end)) },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateLevelGoalForMowIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            LevelGoal with { EntityType = "mow", EntityId = "astraOrdnanceBattery" },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCombinedLevelGoalIsAccepted()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals/combined",
            new CreateCombinedGoalsRequest(
                "character",
                CharacterId,
                null,
                [
                    new CombinedGoalSpec(
                        "level",
                        new CreateGoalConfigRequest(Level: new LevelTargetRequest(1, 15)),
                        [])
                ]
            ),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreateCombinedGoalsResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(created);
        var goal = Assert.Single(created.Goals);
        Assert.Equal("Level", goal.GoalType);
        Assert.Equal(15, goal.Config.Level!.End);
    }
}
