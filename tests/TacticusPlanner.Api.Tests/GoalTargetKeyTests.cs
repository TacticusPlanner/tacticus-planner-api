using TacticusPlanner.Domain.Goals;
using TacticusPlanner.GameDomain;

namespace TacticusPlanner.Api.Tests;

public sealed class GoalTargetKeyTests
{
    [Fact]
    public void RankDelegatesToRankTargetKey()
    {
        var config = new GoalConfig { Rank = new RankTarget { Start = 1, End = (int)UnitRank.Silver3, EndPointFive = true } };
        Assert.Equal(RankTargetKey.From((int)UnitRank.Silver3, false, 3), GoalTargetKey.For(GoalType.Rank, config));
    }

    [Fact]
    public void AscensionStartDoesNotDistinguish() =>
        Assert.Equal(
            GoalTargetKey.For(GoalType.Ascension, Ascension("Epic:RedThreeStars", "Legendary:RedFiveStars")),
            GoalTargetKey.For(GoalType.Ascension, Ascension("Legendary:RedThreeStars", "Legendary:RedFiveStars")));

    [Fact]
    public void AscensionEndDistinguishes() =>
        Assert.NotEqual(
            GoalTargetKey.For(GoalType.Ascension, Ascension("Epic:RedThreeStars", "Legendary:RedFiveStars")),
            GoalTargetKey.For(GoalType.Ascension, Ascension("Epic:RedThreeStars", "Mythic:OneBlueStar")));

    [Fact]
    public void AbilityKeyIsActiveAndPassiveEnd() =>
        Assert.Equal("50:45", GoalTargetKey.For(GoalType.Ability, Ability(40, 50, 40, 45)));

    [Fact]
    public void UnadvancedAbilityTrackDoesNotDistinguish()
    {
        var first = GoalTargetKey.For(GoalType.Ability, Ability(40, 50, 40, 40));
        var second = GoalTargetKey.For(GoalType.Ability, Ability(40, 50, 42, 42));
        Assert.Equal("50:-", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void UpgradeTargetOrderDoesNotDistinguish()
    {
        var range = new UpgradeRange { Start = 4, End = 9 };
        Assert.Equal(
            GoalTargetKey.For(GoalType.Upgrade, Upgrade(range, ("upgA", 3), ("upgB", 2))),
            GoalTargetKey.For(GoalType.Upgrade, Upgrade(range, ("upgB", 2), ("upgA", 3))));
    }

    [Fact]
    public void UpgradeQuantityDistinguishes() =>
        Assert.NotEqual(
            GoalTargetKey.For(GoalType.Upgrade, Upgrade(null, ("upgA", 3))),
            GoalTargetKey.For(GoalType.Upgrade, Upgrade(null, ("upgA", 5))));

    [Fact]
    public void UpgradeRangeDistinguishes() =>
        Assert.NotEqual(
            GoalTargetKey.For(GoalType.Upgrade, Upgrade(new UpgradeRange { Start = 4, End = 9 }, ("upgA", 3))),
            GoalTargetKey.For(GoalType.Upgrade, Upgrade(null, ("upgA", 3))));

    [Fact]
    public void UnlockAndMissingTargetsHaveNoKey()
    {
        Assert.Null(GoalTargetKey.For(GoalType.Unlock, new GoalConfig()));
        Assert.Null(GoalTargetKey.For(GoalType.Ascension, new GoalConfig()));
        Assert.Null(GoalTargetKey.For(GoalType.Ability, new GoalConfig()));
        Assert.Null(GoalTargetKey.For(GoalType.Upgrade, new GoalConfig()));
    }

    private static GoalConfig Ascension(string start, string end) =>
        new() { Progression = new ProgressionTarget { Start = start, End = end } };

    private static GoalConfig Ability(int activeStart, int activeEnd, int passiveStart, int passiveEnd) =>
        new() { Ability = new AbilityTarget { ActiveStart = activeStart, ActiveEnd = activeEnd, PassiveStart = passiveStart, PassiveEnd = passiveEnd } };

    private static GoalConfig Upgrade(UpgradeRange? rankRange, params (string Id, int Quantity)[] targets) =>
        new()
        {
            Upgrade = new UpgradeTarget
            {
                RankRange = rankRange,
                Targets = targets.Select(item => new UpgradeMaterialTarget { UpgradeId = item.Id, Quantity = item.Quantity }).ToList(),
            },
        };
}
