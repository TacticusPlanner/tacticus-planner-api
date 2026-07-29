using TacticusPlanner.GameDomain;

namespace TacticusPlanner.Api.Tests;

public sealed class ProgressionRulesTests
{
    [Theory]
    [InlineData(UnitProgression.CommonTwoStars, "Common", 8)]
    [InlineData(UnitProgression.UncommonTwoStars, "Uncommon", 17)]
    [InlineData(UnitProgression.RareRedOneStar, "Rare", 26)]
    [InlineData(UnitProgression.EpicRedThreeStars, "Epic", 35)]
    [InlineData(UnitProgression.LegendaryOneBlueStar, "Legendary", 50)]
    [InlineData(UnitProgression.MythicMythicWings, "Mythic", 60)]
    public void ResolvesRarityAndAbilityCapFromTypedProgression(
        UnitProgression progression,
        string expectedRarity,
        int expectedCap)
    {
        Assert.Equal(expectedRarity, ProgressionRules.RarityFor(progression));
        Assert.Equal(expectedCap, ProgressionRules.AbilityCapForProgression(progression));
    }

    [Fact]
    public void RejectsProgressionOutsideTheKnownLadder()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProgressionRules.AbilityCapForProgression((UnitProgression)(-1)));
    }
}
