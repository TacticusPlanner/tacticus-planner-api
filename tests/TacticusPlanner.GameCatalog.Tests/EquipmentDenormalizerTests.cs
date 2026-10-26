using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class EquipmentDenormalizerTests
{
    private static readonly string[] GameOnlyStatNames = ["critDmg", "critDmgBonus", "blockDmg", "blockDmgBonus", "fixedArmor"];

    private static DatamineItem Item(string type, string rarity, params DatamineItemLevel[] levels) =>
        new("Name", type, rarity, levels, null, null, null, null, null);

    [Fact]
    public void StatNamesAreMappedToTheServedNames()
    {
        var items = new Dictionary<string, DatamineItem>(StringComparer.Ordinal)
        {
            ["I_Crit_C001"] = Item("I_Crit", "Common", new DatamineItemLevel(
                new Dictionary<string, int> { ["critChance"] = 5, ["critDmg"] = 10, ["critDmgBonus"] = 1, ["blockDmg"] = 2, ["blockDmgBonus"] = 3, ["fixedArmor"] = 4 },
                null, null, null)),
        };

        var stats = Assert.Single(Assert.Single(GameCatalogDenormalizer.ReadDatamineEquipment(items)["I_Crit"]).Levels).Stats;

        Assert.Equal(
            new Dictionary<string, int> { ["critChance"] = 5, ["critDamage"] = 10, ["critDamageBonus"] = 1, ["blockDamage"] = 2, ["blockDamageBonus"] = 3, ["armor"] = 4 },
            stats);
    }

    [Fact]
    public void LevelCostsBecomeTheUpgradeLadderAndMissingFieldsDefault()
    {
        var items = new Dictionary<string, DatamineItem>(StringComparer.Ordinal)
        {
            ["I_Block_M001"] = Item("I_Block", "Mythic",
                new DatamineItemLevel(new Dictionary<string, int>(), 100, null, null),
                new DatamineItemLevel(new Dictionary<string, int>(), 200, 30, 4)),
        };

        var item = Assert.Single(GameCatalogDenormalizer.ReadDatamineEquipment(items)["I_Block"]);

        Assert.Equal([new GameCatalogEquipmentUpgradeLevel(100, 0, 0), new GameCatalogEquipmentUpgradeLevel(200, 30, 4)], item.UpgradeLevels);
        Assert.Equal(string.Empty, item.AbilityId);
        Assert.False(item.IsRelic);
        Assert.False(item.IsUniqueRelic);
        Assert.Empty(item.AllowedUnits);
        Assert.Empty(item.AllowedFactions);
    }

    [Fact]
    public void ServedEquipmentIsOrderedByTypeThenId()
    {
        var views = GameCatalogLoader.Load().EquipmentViews;

        Assert.Equal(217, views.Count);
        Assert.Equal(
            views.OrderBy(item => item.Type, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).Select(item => item.Id),
            views.Select(item => item.Id));
    }

    [Fact]
    public void ServedEquipmentUsesOnlyServedStatNames()
    {
        var statNames = GameCatalogLoader.Load().EquipmentViews
            .SelectMany(item => item.Levels)
            .SelectMany(level => level.Stats.Keys)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(statNames, GameOnlyStatNames.Contains);
    }

    [Fact]
    public void EachRarityHasOneUpgradeLadder()
    {
        // The game prices levels per item, but every item of a rarity shares one ladder (as the retired
        // equipment-upgrade-costs.json had it). A datamine bump that breaks this is worth a look.
        foreach (var rarity in GameCatalogLoader.Load().EquipmentViews.GroupBy(item => item.Rarity, StringComparer.Ordinal))
        {
            var ladders = rarity.Select(item => item.UpgradeLevels).ToList();
            Assert.All(ladders, ladder => Assert.Equal(ladders[0], ladder));
        }
    }
}
