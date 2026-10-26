using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class UpgradeDenormalizerTests
{
    private static GameCatalogCampaignBattle Battle(
        string id,
        IReadOnlyList<GameCatalogCampaignGuaranteedReward> guaranteed,
        int energyCost = 6,
        int nodeNumber = 1,
        string type = "Standard") =>
        new(id, type, false, energyCost, nodeNumber, 5, new GameCatalogCampaignRewards(guaranteed, []),
            0, [], [], 0, [], [], []);

    private static GameCatalogCampaignGroup Group(string groupId, params GameCatalogCampaignBattle[] battles) =>
        new(groupId, "AdeptusMechanicus", "standard", "Imperial", ["AdeptusMechanicus"], [], ["Standard"], battles);

    private static GameCatalogUpgrade Upgrade(string id) =>
        new(id, id, id, id, "Common", "Armour", false, []);

    private static IReadOnlyList<GameCatalogUpgradeView> BuildUpgrades(
        IReadOnlyDictionary<string, GameCatalogCampaignGroup> campaignGroups,
        params GameCatalogUpgrade[] upgrades) =>
        GameCatalogDenormalizer.BuildUpgrades(
            new Dictionary<string, IReadOnlyList<GameCatalogUpgrade>> { ["upgrades-common"] = upgrades },
            campaignGroups,
            []);

    [Fact]
    public void TwoBattlesTyingOnDropRateReportTheirOwnDistinctExpectedGold()
    {
        var battleA = Battle("AMS12", [
            new GameCatalogCampaignGuaranteedReward("gold", 109, 165),
            new GameCatalogCampaignGuaranteedReward("upgArmU013", 1, 1),
        ]);
        var battleB = Battle("SHME19", [
            new GameCatalogCampaignGuaranteedReward("gold", 123, 180),
            new GameCatalogCampaignGuaranteedReward("upgArmU013", 1, 1),
        ]);
        var groups = new Dictionary<string, GameCatalogCampaignGroup>
        {
            ["g1"] = Group("g1", battleA),
            ["g2"] = Group("g2", battleB),
        };

        var view = Assert.Single(BuildUpgrades(groups, Upgrade("upgArmU013")));

        var locationA = Assert.Single(view.FarmLocations, location => location.BattleId == "AMS12");
        var locationB = Assert.Single(view.FarmLocations, location => location.BattleId == "SHME19");
        Assert.Equal(137, locationA.ExpectedGold);
        Assert.Equal(151.5, locationB.ExpectedGold);
    }

    [Fact]
    public void ABattleWithNoGuaranteedGoldRewardReportsNull()
    {
        var battle = Battle("AMS12", [new GameCatalogCampaignGuaranteedReward("upgArmC006", 1, 1)]);
        var groups = new Dictionary<string, GameCatalogCampaignGroup> { ["g1"] = Group("g1", battle) };

        var view = Assert.Single(BuildUpgrades(groups, Upgrade("upgArmC006")));

        Assert.Null(Assert.Single(view.FarmLocations).ExpectedGold);
    }

    [Fact]
    public void TwoDifferentResourcesDroppedByTheSameBattleReportTheSameExpectedGold()
    {
        var battle = Battle("AMS12", [
            new GameCatalogCampaignGuaranteedReward("gold", 48, 82),
            new GameCatalogCampaignGuaranteedReward("upgArmC006", 1, 1),
            new GameCatalogCampaignGuaranteedReward("upgArmC011", 1, 1),
        ]);
        var groups = new Dictionary<string, GameCatalogCampaignGroup> { ["g1"] = Group("g1", battle) };

        var upgrades = BuildUpgrades(groups, Upgrade("upgArmC006"), Upgrade("upgArmC011"));

        var expectedGoldA = Assert.Single(upgrades.Single(u => u.Id == "upgArmC006").FarmLocations).ExpectedGold;
        var expectedGoldB = Assert.Single(upgrades.Single(u => u.Id == "upgArmC011").FarmLocations).ExpectedGold;
        Assert.Equal(65, expectedGoldA);
        Assert.Equal(expectedGoldA, expectedGoldB);
    }

    [Fact]
    public void DatamineUpgradesAreGroupedBySourceKeyAndNamedFromEnglish()
    {
        var byKey = GameCatalogDenormalizer.ReadDatamineUpgrades(
            new Dictionary<string, DatamineUpgrade>
            {
                ["upgHpU005C"] = new("dev", "Uncommon", "hp", [new DatamineUpgradeIngredient("upgHpC007", 6)]),
                ["upgHpU004C"] = new("dev", "Uncommon", "hp", [new DatamineUpgradeIngredient("upgHpC002", 6)]),
                ["upgArmU001"] = new("dev", "Uncommon", "fixedArmor", null),
            },
            new Dictionary<string, string>
            {
                ["Upgrades/upgHpU005C_name"] = "First Aid Kit",
                ["Upgrades/upgHpU004C_name"] = "Bionic Enhancement",
                ["Upgrades/upgArmU001_name"] = "Oath Seal",
            });

        Assert.Equal(["upgrades-uncommon", "upgrades-uncommon-crafted"], byKey.Keys.Order(StringComparer.Ordinal));
        var oathSeal = Assert.Single(byKey["upgrades-uncommon"]);
        Assert.Equal(("upgArmU001", "Oath Seal", "Oath Seal", "Armour"), (oathSeal.SnowprintId, oathSeal.Label, oathSeal.Material, oathSeal.Stat));
        Assert.False(oathSeal.Craftable);
        Assert.Empty(oathSeal.Recipe);
        var crafted = byKey["upgrades-uncommon-crafted"];
        Assert.Equal(["upgHpU004C", "upgHpU005C"], crafted.Select(upgrade => upgrade.Id));
        Assert.Equal("Health", crafted[0].Stat);
        Assert.True(crafted[0].Craftable);
        Assert.Equal(new GameCatalogUpgradeRecipeIngredient("upgHpC002", 6), Assert.Single(crafted[0].Recipe));
    }

    [Theory]
    [InlineData("hp", false)]
    [InlineData("speed", true)]
    public void AMissingEnglishNameOrUnknownStatFailsTheLoad(string statType, bool hasName)
    {
        var english = hasName
            ? new Dictionary<string, string> { ["Upgrades/upgX_name"] = "X" }
            : [];

        var exception = Assert.Throws<InvalidOperationException>(() => GameCatalogDenormalizer.ReadDatamineUpgrades(
            new Dictionary<string, DatamineUpgrade> { ["upgX"] = new("dev", "Common", statType, null) },
            english));

        Assert.Contains("upgrade 'upgX'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShippedUpgradesUseTheGamesAccentedNames()
    {
        var snapshot = GameCatalogLoader.Load();

        Assert.Equal("Mjød", snapshot.Upgrades.Single(upgrade => upgrade.Id == "upgHpL111").Label);
        Assert.Equal(560, snapshot.Upgrades.Count);
    }
}
