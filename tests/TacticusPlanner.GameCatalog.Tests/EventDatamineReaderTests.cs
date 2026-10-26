using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class EventDatamineReaderTests
{
    private static readonly int[] HeroRewardSteps = [1, 2, 4, 5, 7, 8, 10, 11, 14, 15, 18, 19];

    private static DatamineOnslaught Onslaught(Func<int, string>? shards = null, params (int Round, string Reward)[] rounds) =>
        new(
            [new DatamineOnslaughtTier(1)],
            [.. rounds.SelectMany(round => new[]
            {
                new DatamineOnslaughtWave(1, round.Round, new Dictionary<string, string> { ["Imperial"] = round.Reward.Replace("_X", "_Chaos", StringComparison.Ordinal) }),
                new DatamineOnslaughtWave(1, round.Round, new Dictionary<string, string> { ["Chaos"] = round.Reward.Replace("_X", "_Xenos", StringComparison.Ordinal) }),
            })],
            [.. HeroRewardSteps.Select(step => new DatamineOnslaughtHeroRewards(
                1, step, [shards?.Invoke(step) ?? (step >= 15 ? "mythicShards_[heroId]:1-2" : $"shards_[heroId]:{step}-{step + 1}")]))]);

    [Fact]
    public void AnOnslaughtTierTakesTheTopStepOfEachRarityAndSumsItsRoundBadges()
    {
        var reward = Assert.Single(GameCatalogDenormalizer.ReadDatamineOnslaughtRewards(
            Onslaught(null, (1, "abilityTokenCommon_X"), (3, "abilityTokenUncommon_X:2"), (4, "abilityTokenCommon_X"))));

        Assert.Equal(("Stone-1", "Stone", 1), (reward.Id, reward.Sector, reward.Tier));
        Assert.Equal([(2, 3), (5, 6), (8, 9), (11, 12), (14, 15)], reward.Regular.Select(range => (range.Min, range.Max)));
        Assert.Equal((1, 2), (reward.Mythic.Min, reward.Mythic.Max));
        Assert.Equal([new GameCatalogAmountByRarity("Common", 2), new GameCatalogAmountByRarity("Uncommon", 2)], reward.Badges);
    }

    [Fact]
    public void AnOnslaughtRoundWhoseWavesDisagreeFails()
    {
        var onslaught = Onslaught(null, (1, "abilityTokenCommon_X"));
        onslaught = onslaught with
        {
            TierWaves = [.. onslaught.TierWaves, new DatamineOnslaughtWave(1, 1, new Dictionary<string, string> { ["Xenos"] = "abilityTokenRare_Imperial" })],
        };

        var error = Assert.Throws<InvalidOperationException>(() => GameCatalogDenormalizer.ReadDatamineOnslaughtRewards(onslaught));

        Assert.Contains("onslaught-rewards", error.Message, StringComparison.Ordinal);
        Assert.Contains("round 1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOnslaughtTierWithoutAShardRewardFails()
    {
        var error = Assert.Throws<InvalidOperationException>(() => GameCatalogDenormalizer.ReadDatamineOnslaughtRewards(
            Onslaught(step => step == 8 ? "gold:100" : $"shards_[heroId]:{step}", (1, "abilityTokenCommon_X"))));

        Assert.Contains("progression index 8", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void QuestEventsReadTheEnemyFactionDropsEnemiesAndCuratedPower()
    {
        var battle = new DatamineLinearHeroBattle(
            "1", "C3_70", 6, 3, new DatamineLinearHeroLoot("upgDmgC001%3/4"), ["necroNpc1Warrior:2", "PowerUp"], []);
        var quests = GameCatalogDenormalizer.ReadDatamineQuestEvents(
            [new DatamineLinearHeroEvent("astarCyrus", """{"enemyFaction":"Necrons"}""", ["Ultramarines"], [new DatamineLinearHeroTier(0, [battle])])],
            new Dictionary<string, Dictionary<string, int>> { ["astarCyrus"] = new() { ["1"] = 260 } });

        var quest = Assert.Single(quests);
        Assert.Equal(["Necrons"], quest.EnemyFactions);
        var served = Assert.Single(Assert.Single(quest.Tiers).Battles);
        Assert.Equal(("upgDmgC001", 3, 4, (int?)260), (served.Loot.Reward.UpgradeId, served.Loot.Reward.Num, served.Loot.Reward.Denom, served.Power));
        Assert.Equal([("necroNpc1Warrior", (int?)2), ("PowerUp", null)], served.Enemies.Select(enemy => (enemy.Name, enemy.ProgressionIndex)));
    }

    [Fact]
    public void ACuratedPowerForAnUnknownBattleFails()
    {
        var battle = new DatamineLinearHeroBattle("1", "C3_70", 6, 3, new DatamineLinearHeroLoot("upgDmgC001%3/4"), [], []);

        var error = Assert.Throws<InvalidOperationException>(() => GameCatalogDenormalizer.ReadDatamineQuestEvents(
            [new DatamineLinearHeroEvent("astarCyrus", """{"enemyFaction":"Necrons"}""", [], [new DatamineLinearHeroTier(0, [battle])])],
            new Dictionary<string, Dictionary<string, int>> { ["astarCyrus"] = new() { ["18"] = 1 } }));

        Assert.Contains("quest-events", error.Message, StringComparison.Ordinal);
        Assert.Contains("'18'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippedHseScoringSkipsTheDefeatWavesLowTierTheGameNoLongerHas()
    {
        var scoring = GameCatalogLoader.Load().HseScoring.Single(definition => definition.DefinitionId == "hse-defeat-waves");

        Assert.Equal(["High", "Mid"], scoring.Variants.Single(variant => variant.VariantKey == "default").Tiers.Select(tier => tier.Tier));
        Assert.Equal(["High", "Mid", "Low"], scoring.Variants.Single(variant => variant.VariantKey == "run-2").Tiers.Select(tier => tier.Tier));
    }
}
