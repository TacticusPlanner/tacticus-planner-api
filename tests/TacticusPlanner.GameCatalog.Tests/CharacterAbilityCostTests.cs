using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class CharacterAbilityCostTests
{
    private static GameCatalogCharacterAbilityCost[] Ladder(int count) =>
        Enumerable.Range(1, count).Select(lvl => new GameCatalogCharacterAbilityCost(lvl, 10, 1)).ToArray();

    [Fact]
    public void ShippedLadderMatchesV1RowCountAndSampledRows()
    {
        var snapshot = GameCatalogLoader.Load();

        Assert.Equal(59, snapshot.CharacterAbilityCosts.Count);
        // Sampled from V1's characters-lvl-up-abilities.json.
        Assert.Equal(new GameCatalogCharacterAbilityCost(1, 25, 1), snapshot.CharacterAbilityCosts[0]);
        Assert.Equal(new GameCatalogCharacterAbilityCost(4, 150, 2), snapshot.CharacterAbilityCosts[3]);
        Assert.Equal(new GameCatalogCharacterAbilityCost(9, 600, 1), snapshot.CharacterAbilityCosts[8]);
        Assert.Equal(new GameCatalogCharacterAbilityCost(59, 150000, 5), snapshot.CharacterAbilityCosts[58]);
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
    public void RarityBandsFollowV1Boundaries(int level, string rarity)
    {
        var view = GameCatalogDenormalizer.BuildCharacterAbilityCosts(Ladder(59)).Single(view => view.Level == level);

        Assert.Equal(rarity, view.Badges.Rarity);
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
    [InlineData(-1, 1)]
    [InlineData(10, 0)]
    public void NegativeGoldOrNonPositiveBadgesFail(int gold, int badges)
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateCharacterAbilityCosts([new GameCatalogCharacterAbilityCost(1, gold, badges)], errors);

        Assert.Contains(errors, error => error.Code == "InvalidCost");
    }
}
