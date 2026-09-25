using TacticusPlanner.Domain.Goals;
using TacticusPlanner.GameDomain;

namespace TacticusPlanner.Api.Tests;

public sealed class RankTargetKeyTests
{
    private const int Silver3 = (int)UnitRank.Silver3;

    [Theory]
    [InlineData(false, 3, true, 0)] // three applied slots == point-five below Adamantine1
    [InlineData(false, 7, false, 3)] // applied slots cap at the 3-slot row below Adamantine1
    public void EquivalentEndStatesShareOneKey(bool pointFiveA, int appliedA, bool pointFiveB, int appliedB) =>
        Assert.Equal(
            RankTargetKey.From(Silver3, pointFiveA, appliedA),
            RankTargetKey.From(Silver3, pointFiveB, appliedB));

    [Theory]
    [InlineData(false, 0, false, 2)] // clean vs partially applied
    [InlineData(false, 2, true, 0)] // partial vs full row
    public void DistinctEndStatesHaveDistinctKeys(bool pointFiveA, int appliedA, bool pointFiveB, int appliedB) =>
        Assert.NotEqual(
            RankTargetKey.From(Silver3, pointFiveA, appliedA),
            RankTargetKey.From(Silver3, pointFiveB, appliedB));

    [Fact]
    public void DifferentRanksAreDistinctEvenWithTheSameSlots() =>
        Assert.NotEqual(
            RankTargetKey.From(Silver3, false, 0),
            RankTargetKey.From((int)UnitRank.Gold1, false, 0));

    [Fact]
    public void PointFiveIsIgnoredFromAdamantine1() =>
        Assert.Equal(
            RankTargetKey.From((int)UnitRank.Adamantine1, true, 2),
            RankTargetKey.From((int)UnitRank.Adamantine1, false, 2));

    [Fact]
    public void OnlyRankGoalsWithATargetGetAKey()
    {
        var config = new GoalConfig { Rank = new RankTarget { Start = 1, End = Silver3 } };
        Assert.Equal($"{Silver3}:0", RankTargetKey.For(GoalType.Rank, config));
        Assert.Null(RankTargetKey.For(GoalType.Ability, config));
        Assert.Null(RankTargetKey.For(GoalType.Rank, new GoalConfig()));
    }
}
