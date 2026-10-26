using System.Text.Json;
using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class MythicQuestDenormalizerTests
{
    private static GameCatalogRawMythicQuests Character(string id, params int[] numbers) => new(
        id,
        numbers.Select(number => new GameCatalogRawMythicQuest(
            number,
            ["gold:1"],
            [new GameCatalogRawMythicQuestTask("DealDamageWithHero_description", 1, new Dictionary<string, string> { ["heroId"] = id })]))
            .ToArray());

    [Fact]
    public void ShippedDatasetCovers119CharactersWithFiveQuestsEach()
    {
        var views = GameCatalogLoader.Load().MythicQuestViews;

        Assert.Equal(119, views.Count);
        Assert.All(views, view => Assert.Equal([1, 2, 3, 4, 5], view.Quests.Select(quest => quest.Number)));
        Assert.Equal(views.Select(view => view.UnitId).Order(StringComparer.Ordinal), views.Select(view => view.UnitId));
    }

    [Fact]
    public void AbaddonQuestOneIsServedWithParametersAndStructuredRewards()
    {
        var abaddon = GameCatalogLoader.Load().MythicQuestViews.Single(view => view.UnitId == "blackAbaddon");
        var quest = abaddon.Quests[0];

        Assert.Equal(1, quest.Number);
        var task = Assert.Single(quest.Tasks);
        Assert.Equal("SlayEnemiesWithHeroToTraitInOneBattle_description", task.LocaKey);
        Assert.Equal(1, task.Target);
        Assert.Equal(
            new Dictionary<string, string> { ["trait"] = "Emplacement", ["heroId"] = "blackAbaddon", ["killAmount"] = "8" },
            task.Parameters);
        Assert.Equal(
            [
                new GameCatalogMythicQuestRewardView("MythicShards", 4, UnitId: "blackAbaddon"),
                new GameCatalogMythicQuestRewardView("AscensionOrb", 2, Rarity: "Mythic", Alliance: "Chaos"),
                new GameCatalogMythicQuestRewardView("XpBook", 1, Rarity: "Mythic"),
            ],
            quest.Rewards);
    }

    [Fact]
    public void AbaddonQuestFiveHasAnEquipmentRewardWithAmountOne()
    {
        var abaddon = GameCatalogLoader.Load().MythicQuestViews.Single(view => view.UnitId == "blackAbaddon");

        Assert.Equal(new GameCatalogMythicQuestRewardView("Equipment", 1, EquipmentId: "I_Crit_M006"), abaddon.Quests[4].Rewards[^1]);
        Assert.Equal(3, abaddon.Quests[4].Tasks.Count);
    }

    [Theory]
    [InlineData("mythicShards_blackAbaddon:4", "MythicShards", 4, "blackAbaddon", null, null, null)]
    [InlineData("heroAscensionOrbMythic_Imperial:2", "AscensionOrb", 2, null, "Mythic", "Imperial", null)]
    [InlineData("abilityTokenMythic_Xenos:3", "AbilityBadge", 3, null, "Mythic", "Xenos", null)]
    [InlineData("xpMythic:1", "XpBook", 1, null, "Mythic", null, null)]
    [InlineData("gold:250000", "Gold", 250000, null, null, null, null)]
    [InlineData("R_Booster_Crit_BookOfFate", "Equipment", 1, null, null, null, "R_Booster_Crit_BookOfFate")]
    public void EachRewardPatternParses(
        string raw, string type, int amount, string? unitId, string? rarity, string? alliance, string? equipmentId)
    {
        Assert.True(MythicQuestRewardParser.TryParse(raw, out var view));
        Assert.Equal(new GameCatalogMythicQuestRewardView(type, amount, unitId, rarity, alliance, equipmentId), view);
    }

    [Theory]
    [InlineData("blueStarMythic:1")]
    [InlineData("heroAscensionOrbMythic_Necrons:2")]
    [InlineData("gold:0")]
    [InlineData("gold:abc")]
    public void UnknownOrMalformedRewardsDoNotParse(string raw) =>
        Assert.False(MythicQuestRewardParser.TryParse(raw, out _));

    [Fact]
    public void EveryShippedRewardStringParses()
    {
        var raw = GameCatalogLoader.Load().MythicQuests;
        var rewards = raw.SelectMany(character => character.Quests).SelectMany(quest => quest.Rewards).ToArray();

        Assert.Equal(1785, rewards.Length);
        Assert.All(rewards, reward => Assert.True(MythicQuestRewardParser.TryParse(reward, out _), reward));
    }

    [Fact]
    public void RecordsAreOrderedByUnitIdAndQuestsByNumber()
    {
        var views = GameCatalogDenormalizer.BuildMythicQuests([Character("ultraCalgar", 2, 1), Character("blackAbaddon", 1)]);

        Assert.Equal(["blackAbaddon", "ultraCalgar"], views.Select(view => view.UnitId));
        Assert.Equal([1, 2], views[1].Quests.Select(quest => quest.Number));
    }

    [Fact]
    public void ServedJsonCarriesOnlyTheFieldsEachRewardTypeNeeds()
    {
        var json = JsonSerializer.Serialize(
            new GameCatalogMythicQuestRewardView("Gold", 250000), JsonSerializerOptions.Web);

        Assert.Equal("""{"type":"Gold","amount":250000}""", json);
    }
}
