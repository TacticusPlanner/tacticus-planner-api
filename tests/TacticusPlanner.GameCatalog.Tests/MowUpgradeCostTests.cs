using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class MowUpgradeCostTests
{
    private static GameCatalogMowUpgradeCost ReadRow(Dictionary<string, int> row) =>
        Assert.Single(GameCatalogDenormalizer.ReadDatamineMowUpgradeCosts([row]));

    [Fact]
    public void ShippedLadderMatchesTheDatamineRowCountAndSampledRows()
    {
        var views = GameCatalogLoader.Load().MowUpgradeCostViews;

        Assert.Equal(Enumerable.Range(2, 59), views.Select(view => view.Level));
        // Sampled from the datamine units/abilityUpgradeCostsMoW.json.
        Assert.Equal(new GameCatalogMowUpgradeCostView(2, 15, 5, new("Common", 1), null, 1), views[0]);
        Assert.Equal(new GameCatalogMowUpgradeCostView(9, 300, 0, new("Uncommon", 1), new("Uncommon", 1), 3), views[7]);
        Assert.Equal(new GameCatalogMowUpgradeCostView(60, 125000, 0, new("Mythic", 5), new("Mythic", 5), 50), views[^1]);
    }

    [Fact]
    public void RowKeysMapToTheServedFields()
    {
        var cost = ReadRow(new()
        {
            ["gold"] = 300,
            ["dust"] = 4,
            ["abilityTokenEpic"] = 2,
            ["itemAscensionResource_Epic"] = 1,
            ["machinesOfWarToken"] = 16,
        });

        Assert.Equal(new GameCatalogMowUpgradeCost(300, 4, new("Epic", 2), new("Epic", 1), 16), cost);
    }

    [Fact]
    public void MissingDustAndForgeBadgesDefault()
    {
        var cost = ReadRow(new() { ["gold"] = 15, ["abilityTokenCommon"] = 1, ["machinesOfWarToken"] = 1 });

        Assert.Equal(new GameCatalogMowUpgradeCost(15, 0, new("Common", 1), null, 1), cost);
    }

    [Theory]
    [InlineData("gold")]
    [InlineData("machinesOfWarToken")]
    [InlineData("abilityTokenCommon")]
    public void RowMissingARequiredKeyFailsStartup(string missing)
    {
        var row = new Dictionary<string, int> { ["gold"] = 15, ["abilityTokenCommon"] = 1, ["machinesOfWarToken"] = 1 };
        row.Remove(missing);

        var error = Assert.Throws<InvalidOperationException>(() => ReadRow(row));
        Assert.Contains(GameCatalogDatasets.MowUpgradeCostsServed, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("abilityTokenRare")]
    [InlineData("itemAscensionResource_Epic")]
    [InlineData("blessedSalvage")]
    public void RowWithAnExtraOrUnknownKeyFailsStartup(string extra)
    {
        var row = new Dictionary<string, int>
        {
            ["gold"] = 15,
            ["abilityTokenCommon"] = 1,
            ["itemAscensionResource_Common"] = 1,
            ["machinesOfWarToken"] = 1,
            [extra] = 1,
        };

        Assert.Throws<InvalidOperationException>(() => ReadRow(row));
    }
}
