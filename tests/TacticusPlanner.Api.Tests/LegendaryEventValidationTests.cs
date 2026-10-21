using TacticusPlanner.Domain.LegendaryEvents;

namespace TacticusPlanner.Api.Tests;

/// <summary>The Legendary Event plan invariants that replaced the dropped child-table CHECK constraints: every value
/// those constraints rejected is rejected before save.</summary>
public sealed class LegendaryEventValidationTests
{
    private static LegendaryEventPlan ValidPlan() => new()
    {
        Id = LegendaryEventPlanId.From(Guid.CreateVersion7()),
        EventId = "votanUthar",
        CatalogVersion = "test",
        Teams =
        [
            Team("alpha", 0),
            Team("alpha", 1),
            Team("beta", 0),
        ],
        Runs = [new() { Run = 1, RegularMissions = 2, CloseShards = 25 }],
        Annotations = [new() { LaneId = "gamma", BattleIndex = 3, ObjectiveId = 5 }],
    };

    private static LegendaryEventTeamRecord Team(string lane, int sortOrder) => new()
    {
        Id = LegendaryEventTeamId.From(Guid.CreateVersion7()),
        LaneId = lane,
        Name = $"{lane} {sortOrder}",
        SortOrder = sortOrder,
        MemberUnitIds = ["a", "b", "c", "d", "e"],
        ReserveUnitId = "f",
        ObjectiveIndexes = [0, 2],
        RunDepths = [new() { Run = 1, ExpectedBattleClears = 1 }, new() { Run = 3, ExpectedBattleClears = 12 }],
    };

    [Fact]
    public void AValidPlanPasses() => LegendaryEventValidation.AssertInvariants(ValidPlan());

    public static TheoryData<string, Action<LegendaryEventPlan>> Violations => new()
    {
        { "unknown lane", plan => plan.Teams[0].LaneId = "delta" },
        { "a sixth line-up member", plan => plan.Teams[0].MemberUnitIds.Add("g") },
        { "a unit twice in a team", plan => plan.Teams[0].MemberUnitIds[1] = "a" },
        { "the reserve also in the line-up", plan => plan.Teams[0].ReserveUnitId = "a" },
        { "a negative objective index", plan => plan.Teams[0].ObjectiveIndexes.Add(-1) },
        { "a repeated objective index", plan => plan.Teams[0].ObjectiveIndexes.Add(2) },
        { "a gap in the lane order", plan => plan.Teams[1].SortOrder = 2 },
        { "two teams at one lane position", plan => plan.Teams[1].SortOrder = 0 },
        { "a negative lane position", plan => plan.Teams[2].SortOrder = -1 },
        { "run 0 depth", plan => plan.Teams[0].RunDepths[0].Run = 0 },
        { "run 4 depth", plan => plan.Teams[0].RunDepths[0].Run = 4 },
        { "a zero depth", plan => plan.Teams[0].RunDepths[0].ExpectedBattleClears = 0 },
        { "two depths for one run", plan => plan.Teams[0].RunDepths[1].Run = 1 },
        { "run 4 inputs", plan => plan.Runs[0].Run = 4 },
        { "negative missions", plan => plan.Runs[0].RegularMissions = -1 },
        { "too many close shards", plan => plan.Runs[0].CloseShards = LegendaryEventValidation.MaxCloseShards + 1 },
        { "an unknown annotation lane", plan => plan.Annotations[0].LaneId = "delta" },
        { "a negative battle index", plan => plan.Annotations[0].BattleIndex = -1 },
        { "objective id 6", plan => plan.Annotations[0].ObjectiveId = 6 },
        { "two marks on one cell", plan => plan.Annotations.Add(new() { LaneId = "gamma", BattleIndex = 3, ObjectiveId = 5 }) },
    };

    [Theory]
    [MemberData(nameof(Violations))]
    public void AnInvariantViolationIsRejected(string violation, Action<LegendaryEventPlan> breakPlan)
    {
        var plan = ValidPlan();
        breakPlan(plan);

        var exception = Assert.Throws<InvalidOperationException>(() => LegendaryEventValidation.AssertInvariants(plan));
        Assert.False(string.IsNullOrEmpty(violation));
        Assert.Contains("invariant", exception.Message, StringComparison.Ordinal);
    }
}
