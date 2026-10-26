using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class ProgressionCostTests
{
    private static DatamineHeroProgressionStep Step(int stars, string rarity, params (string Key, int Count)[] costs) =>
        new(stars, rarity, costs.ToDictionary(cost => cost.Key, cost => cost.Count));

    [Fact]
    public void TheFirstStepIsFreeBecauseItsGameCostIsTheUnlock()
    {
        var costs = GameCatalogDenormalizer.ReadDatamineAscensionCosts([
            Step(0, "Common", ("shards", 40)),
            Step(1, "Common", ("shards", 10)),
        ]);

        Assert.Equal(new GameCatalogAscensionCost("Common:None", 0, 0, 0, null), costs[0]);
        Assert.Equal(new GameCatalogAscensionCost("Common:OneStar", 10, 0, 0, null), costs[1]);
    }

    [Fact]
    public void OrbKeysMapToOrbsAndTheirRarity()
    {
        var costs = GameCatalogDenormalizer.ReadDatamineAscensionCosts([
            Step(0, "Common"),
            Step(11, "Mythic", ("mythicShards", 20), ("heroAscensionOrbMythic", 10)),
        ]);

        Assert.Equal(new GameCatalogAscensionCost("Mythic:OneBlueStar", 0, 20, 10, "Mythic"), costs[1]);
    }

    [Theory]
    [InlineData(15, "shards")]
    [InlineData(1, "hreShards")]
    public void AnUnknownStarCountOrCostKeyFailsTheLoad(int stars, string key)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            GameCatalogDenormalizer.ReadDatamineAscensionCosts([Step(0, "Common"), Step(stars, "Common", (key, 1))]));

        Assert.Contains("'ascension-costs' row 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShippedLadderMatchesTheProgressionOrder()
    {
        var snapshot = GameCatalogLoader.Load();
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateAscensionCosts(snapshot.AscensionCosts, errors);

        Assert.Empty(errors);
        Assert.Equal(
            new GameCatalogAscensionCostView("Legendary:OneBlueStar", 500, 0, 20, "Legendary"),
            snapshot.AscensionCostViews.Single(cost => cost.Progression == "Legendary:OneBlueStar"));
    }

    [Fact]
    public void ADroppedStepFailsValidation()
    {
        var snapshot = GameCatalogLoader.Load();
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateAscensionCosts(snapshot.AscensionCosts.Skip(1).ToArray(), errors);

        Assert.Contains(errors, error => error.Code == "UnexpectedProgressions");
    }

    [Fact]
    public void UnlockCostsFollowRarityOrderWhateverTheFileOrder()
    {
        var costs = GameCatalogDenormalizer.ReadDatamineUnlockShardCosts(new Dictionary<string, DatamineHeroConversion>
        {
            ["Rare"] = new(130),
            ["Common"] = new(40),
        });

        Assert.Equal([new GameCatalogUnlockShardCost("Common", 40), new GameCatalogUnlockShardCost("Rare", 130)], costs);
    }

    [Fact]
    public void AnUnknownUnlockRarityFailsTheLoad()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            GameCatalogDenormalizer.ReadDatamineUnlockShardCosts(
                new Dictionary<string, DatamineHeroConversion> { ["Relic"] = new(1) }));

        Assert.Contains("Relic", exception.Message, StringComparison.Ordinal);
    }
}
