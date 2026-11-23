using TacticusPlanner.Domain.Goals;
using TacticusPlanner.GameDomain;

namespace TacticusPlanner.Api.Tests;

public sealed class RankTargetKeyTests
{
    private const int Silver3 = (int)UnitRank.Silver3;

    [Theory]
    [InlineData(false, 3, true, 0)] // three applied slots == point-five below Adamantine1
    [InlineData(true, 4, false, 4)] // point-five is only a floor of 3
    public void EquivalentEndStatesShareOneKey(bool pointFiveA, int appliedA, bool pointFiveB, int appliedB) =>
        Assert.Equal(
            RankTargetKey.From(Silver3, pointFiveA, appliedA),
            RankTargetKey.From(Silver3, pointFiveB, appliedB));

    [Theory]
    [InlineData(false, 0, false, 2)] // clean vs partially applied
    [InlineData(false, 2, true, 0)] // partial vs full row
    [InlineData(false, 4, false, 3)] // a bottom-row slot below Diamond3 is its own stopping point
    [InlineData(false, 5, false, 4)]
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
    public void Diamond2NumbersEachSlot() =>
        Assert.Equal($"{(int)UnitRank.Diamond2}:4", RankTargetKey.From((int)UnitRank.Diamond2, false, 4));

    [Fact]
    public void Diamond3NumbersEachSlot() =>
        Assert.NotEqual(
            RankTargetKey.From((int)UnitRank.Diamond3, false, 4),
            RankTargetKey.From((int)UnitRank.Diamond3, false, 3));

    [Fact]
    public void Diamond3PointFiveStillMeansTheTopRow() =>
        Assert.Equal(
            RankTargetKey.From((int)UnitRank.Diamond3, true, 0),
            RankTargetKey.From((int)UnitRank.Diamond3, false, 3));

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
