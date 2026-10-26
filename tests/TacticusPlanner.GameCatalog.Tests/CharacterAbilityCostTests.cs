using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class CharacterAbilityCostTests
{
    private static GameCatalogCharacterAbilityCost[] Ladder(int count) =>
        Enumerable.Range(1, count)
            .Select(lvl => new GameCatalogCharacterAbilityCost(lvl, 10, new GameCatalogAmountByRarity("Common", 1)))
            .ToArray();

    private static GameCatalogCharacterAbilityCost ReadRow(Dictionary<string, int> row) =>
        Assert.Single(GameCatalogDenormalizer.ReadDatamineCharacterAbilityCosts([row]));

    [Fact]
    public void ShippedLadderMatchesTheDatamineRowCountAndSampledRows()
    {
        var snapshot = GameCatalogLoader.Load();

        Assert.Equal(59, snapshot.CharacterAbilityCosts.Count);
        // Sampled from the datamine units/abilityUpgradeCosts.json.
        Assert.Equal(new GameCatalogCharacterAbilityCost(1, 25, new("Common", 1)), snapshot.CharacterAbilityCosts[0]);
        Assert.Equal(new GameCatalogCharacterAbilityCost(4, 150, new("Common", 2)), snapshot.CharacterAbilityCosts[3]);
        Assert.Equal(new GameCatalogCharacterAbilityCost(9, 600, new("Uncommon", 1)), snapshot.CharacterAbilityCosts[8]);
        // V1 had 127500 at both lvl 50 and 51; the game steps by 2500.
        Assert.Equal(new GameCatalogCharacterAbilityCost(51, 130000, new("Mythic", 1)), snapshot.CharacterAbilityCosts[50]);
        Assert.Equal(new GameCatalogCharacterAbilityCost(52, 132500, new("Mythic", 1)), snapshot.CharacterAbilityCosts[51]);
        Assert.Equal(new GameCatalogCharacterAbilityCost(59, 150000, new("Mythic", 5)), snapshot.CharacterAbilityCosts[58]);
        Assert.Contains(GameCatalogDatasets.CharacterAbilityCostsServed, snapshot.DatasetHashes.Keys);
    }

    [Fact]
    public void ServedLadderIsKeyedByLevelRaisedTo()
    {
        var views = GameCatalogLoader.Load().CharacterAbilityCostViews;

        Assert.Equal(Enumerable.Range(2, 59), views.Select(view => view.Level));
        Assert.Equal(new GameCatalogCharacterAbilityCostView(2, 25, new GameCatalogAmountByRarity("Common", 1)), views[0]);
        Assert.Equal(new GameCatalogCharacterAbilityCostView(60, 150000, new GameCatalogAmountByRarity("Mythic", 5)), views[^1]);
    }

    // The game's token rarities land on the bands V1 hard-coded.
    [Theory]
    [InlineData(2, "Common")]
    [InlineData(8, "Common")]
    [InlineData(9, "Uncommon")]
    [InlineData(17, "Uncommon")]
    [InlineData(18, "Rare")]
    [InlineData(26, "Rare")]
    [InlineData(27, "Epic")]
    [InlineData(35, "Epic")]
    [InlineData(36, "Legendary")]
    [InlineData(50, "Legendary")]
    [InlineData(51, "Mythic")]
    [InlineData(60, "Mythic")]
    public void ShippedRaritiesFollowV1Bands(int level, string rarity)
    {
        var view = GameCatalogLoader.Load().CharacterAbilityCostViews.Single(view => view.Level == level);

        Assert.Equal(rarity, view.Badges.Rarity);
    }

    [Fact]
    public void BadgeRarityComesFromTheTokenKey()
    {
        var cost = ReadRow(new() { ["gold"] = 25, ["abilityTokenEpic"] = 3 });

        Assert.Equal(new GameCatalogCharacterAbilityCost(1, 25, new("Epic", 3)), cost);
    }

    [Theory]
    [InlineData("abilityTokenCommon", "abilityTokenRare")]
    [InlineData("abilityTokenCommon", "dust")]
    [InlineData("dust", null)]
    public void RowWithoutExactlyOneTokenHasNoBadgesAndFailsValidation(string first, string? second)
    {
        var row = new Dictionary<string, int> { ["gold"] = 25, [first] = 1 };
        if (second is not null)
        {
            row[second] = 1;
        }

        var cost = ReadRow(row);
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateCharacterAbilityCosts([cost], errors);

        Assert.Null(cost.Badges);
        Assert.Contains(errors, error => error.Code == "InvalidCost");
    }

    [Fact]
    public void RowWithoutGoldHasNoBadgesAndFailsValidation()
    {
        var cost = ReadRow(new() { ["abilityTokenCommon"] = 1 });
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateCharacterAbilityCosts([cost], errors);

        Assert.Null(cost.Badges);
        Assert.Contains(errors, error => error.Code == "InvalidCost");
    }

    [Fact]
    public void ValidLadderHasNoErrors()
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateCharacterAbilityCosts(Ladder(59), errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void EmptySourceFails()
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateCharacterAbilityCosts([], errors);

        Assert.Contains(errors, error => error.Code == "EmptyDataset");
    }

    [Fact]
    public void GapInLevelsFailsNamingTheDataset()
    {
        var costs = Ladder(6).Where(cost => cost.Lvl != 5).ToArray();
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateCharacterAbilityCosts(costs, errors);

        Assert.Contains(errors, error =>
            error.Code == "NonConsecutiveLevel" && error.Dataset == GameCatalogDatasets.CharacterAbilityCostsServed);
    }

    [Theory]
    [InlineData(-1, 1, "Common")]
    [InlineData(10, 0, "Common")]
    [InlineData(10, 1, "Shiny")]
    public void NegativeGoldNonPositiveBadgesOrUnknownRarityFail(int gold, int badges, string rarity)
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateCharacterAbilityCosts(
            [new GameCatalogCharacterAbilityCost(1, gold, new GameCatalogAmountByRarity(rarity, badges))], errors);

        Assert.Contains(errors, error => error.Code == "InvalidCost");
    }
}
