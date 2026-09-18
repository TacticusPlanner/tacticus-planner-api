using TacticusPlanner.GameDomain;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// Covers the rank-to-minimum-progression, rank-to-required-level, and ability-level-to-minimum-
/// progression tables `rewrite-v1-goal-import` adds to <see cref="ProgressionRules"/> for automatic
/// prerequisite synthesis. Each test's expected values are literal copies of the client's own tables
/// (`packages/game-domain/src/progression.ts`'s <c>maxRankByRarity</c>/<c>abilityCapByRarity</c> and
/// `apps/web/.../goal-farming/lib/rank-additional-target.ts`'s <c>rankToLevel</c>) — the deliberate
/// duplication design.md calls for — so a drift between the two shows up as a failing test rather than
/// silently diverging.
/// </summary>
public sealed class ProgressionRulesPrerequisiteTests
{
    [Theory]
    [InlineData(UnitRank.Stone1, UnitProgression.CommonNone)]
    [InlineData(UnitRank.Stone3, UnitProgression.CommonNone)]
    [InlineData(UnitRank.Iron1, UnitProgression.CommonNone)] // Common's own max rank
    [InlineData(UnitRank.Iron2, UnitProgression.UncommonTwoStars)] // above Common's cap -> Uncommon
    [InlineData(UnitRank.Bronze1, UnitProgression.UncommonTwoStars)] // Uncommon's own max rank
    [InlineData(UnitRank.Bronze2, UnitProgression.RareFourStars)]
    [InlineData(UnitRank.Silver1, UnitProgression.RareFourStars)] // Rare's own max rank
    [InlineData(UnitRank.Silver2, UnitProgression.EpicRedOneStar)]
    [InlineData(UnitRank.Gold1, UnitProgression.EpicRedOneStar)] // Epic's own max rank
    [InlineData(UnitRank.Gold2, UnitProgression.LegendaryRedThreeStars)]
    [InlineData(UnitRank.Diamond3, UnitProgression.LegendaryRedThreeStars)] // Legendary's own max rank
    [InlineData(UnitRank.Adamantine1, UnitProgression.MythicOneBlueStar)]
    [InlineData(UnitRank.Adamantine2, UnitProgression.MythicOneBlueStar)] // Mythic's own max rank
    public void MinimumProgressionForRankMatchesEveryRarityBoundary(UnitRank rank, UnitProgression expected)
    {
        Assert.Equal(expected, ProgressionRules.MinimumProgressionForRank(rank));
    }

    [Fact]
    public void MinimumProgressionForRankAboveTheLadderFallsBackToTheTopRatherThanThrowing()
    {
        Assert.Equal(UnitProgression.MythicMythicWings, ProgressionRules.MinimumProgressionForRank(UnitRank.Adamantine3));
    }

    [Theory]
    [InlineData(UnitProgression.CommonNone, UnitRank.Iron1)]
    [InlineData(UnitProgression.UncommonTwoStars, UnitRank.Bronze1)]
    [InlineData(UnitProgression.RareFourStars, UnitRank.Silver1)]
    [InlineData(UnitProgression.EpicRedOneStar, UnitRank.Gold1)]
    [InlineData(UnitProgression.LegendaryRedThreeStars, UnitRank.Diamond3)]
    [InlineData(UnitProgression.MythicOneBlueStar, UnitRank.Adamantine2)]
    public void MaxRankForProgressionIsTheInverseOfMinimumProgressionForRank(UnitProgression progression, UnitRank expectedMaxRank)
    {
        Assert.Equal(expectedMaxRank, ProgressionRules.MaxRankForProgression(progression));
    }

    [Theory]
    [InlineData(8, UnitProgression.CommonNone)]
    [InlineData(9, UnitProgression.UncommonTwoStars)]
    [InlineData(17, UnitProgression.UncommonTwoStars)]
    [InlineData(18, UnitProgression.RareFourStars)]
    [InlineData(26, UnitProgression.RareFourStars)]
    [InlineData(27, UnitProgression.EpicRedOneStar)]
    [InlineData(35, UnitProgression.EpicRedOneStar)]
    [InlineData(36, UnitProgression.LegendaryRedThreeStars)]
    [InlineData(50, UnitProgression.LegendaryRedThreeStars)]
    [InlineData(51, UnitProgression.MythicOneBlueStar)]
    [InlineData(60, UnitProgression.MythicOneBlueStar)]
    public void MinimumProgressionForAbilityLevelIsTheInverseOfTheAbilityCapTable(int level, UnitProgression expected)
    {
        Assert.Equal(expected, ProgressionRules.MinimumProgressionForAbilityLevel(level));
    }

    [Fact]
    public void MinimumProgressionForAbilityLevelRoundTripsEveryRarityCap()
    {
        foreach (var progression in Enum.GetValues<UnitProgression>())
        {
            var cap = ProgressionRules.AbilityCapForProgression(progression);
            var resolved = ProgressionRules.MinimumProgressionForAbilityLevel(cap);
            // The inverse need not return the same step (several steps share a rarity/cap), but it must
            // resolve to a step whose own cap is exactly the requested one.
            Assert.Equal(cap, ProgressionRules.AbilityCapForProgression(resolved));
        }
    }

    [Fact]
    public void MinimumProgressionForAbilityLevelAboveTheLadderFallsBackToTheTopRatherThanThrowing()
    {
        Assert.Equal(UnitProgression.MythicMythicWings, ProgressionRules.MinimumProgressionForAbilityLevel(61));
    }

    [Theory]
    // Ends of the ladder.
    [InlineData(UnitRank.Stone1, false, 0, 1)]
    [InlineData(UnitRank.Adamantine2, false, 0, 60)]
    // Mid-ladder, pre-Adamantine1 "top row" partial-upgrade cases (Silver1 base level 26).
    [InlineData(UnitRank.Silver1, false, 0, 26)] // clean boundary
    [InlineData(UnitRank.Silver1, false, 1, 26)] // TopRow1
    [InlineData(UnitRank.Silver1, false, 2, 27)] // TopRow2
    [InlineData(UnitRank.Silver1, true, 0, 28)] // TopRow (pointFive)
    [InlineData(UnitRank.Silver1, false, 3, 28)] // TopRow via appliedUpgrades >= 3
    // Adamantine1+ numbered-row partial-upgrade cases (Adamantine1 base level 55).
    [InlineData(UnitRank.Adamantine1, false, 0, 55)]
    [InlineData(UnitRank.Adamantine1, false, 1, 55)] // Row1
    [InlineData(UnitRank.Adamantine1, false, 5, 59)] // Row5
    public void RequiredLevelForRankTargetMatchesTheClientsDecode(
        UnitRank rank, bool endPointFive, int endAppliedUpgrades, int expectedLevel)
    {
        Assert.Equal(expectedLevel, ProgressionRules.RequiredLevelForRankTarget(rank, endPointFive, endAppliedUpgrades));
    }
}
