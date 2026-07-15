using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class GameCatalogLoaderTests
{
    [Fact]
    public void AscensionCostsCoverEveryProgressionStepInLadderOrder()
    {
        var snapshot = GameCatalogLoader.Load();

        // The 20-step (rarity, stars) ascension ladder, ported 1:1 from V1's OrbAscensionCalculator
        // (mirrors the client's `packages/game-domain` progressionOrder ladder).
        string[] expectedOrder =
        [
            "Common:None", "Common:OneStar", "Common:TwoStars",
            "Uncommon:TwoStars", "Uncommon:ThreeStars", "Uncommon:FourStars",
            "Rare:FourStars", "Rare:FiveStars", "Rare:RedOneStar",
            "Epic:RedOneStar", "Epic:RedTwoStars", "Epic:RedThreeStars",
            "Legendary:RedThreeStars", "Legendary:RedFourStars", "Legendary:RedFiveStars", "Legendary:OneBlueStar",
            "Mythic:OneBlueStar", "Mythic:TwoBlueStars", "Mythic:ThreeBlueStars", "Mythic:MythicWings",
        ];

        Assert.Equal(expectedOrder, snapshot.AscensionCostViews.Select(cost => cost.Progression));

        var firstStep = snapshot.AscensionCostViews.Single(cost => cost.Progression == "Common:None");
        Assert.Equal(0, firstStep.Shards);
        Assert.Equal(0, firstStep.Orbs);
        Assert.Null(firstStep.OrbRarity);

        var lastStep = snapshot.AscensionCostViews.Single(cost => cost.Progression == "Mythic:MythicWings");
        Assert.Equal(100, lastStep.MythicShards);
        Assert.Equal(25, lastStep.Orbs);
        Assert.Equal("Mythic", lastStep.OrbRarity);
    }

    [Fact]
    public void UnlockShardCostsCoverEveryRarity()
    {
        var snapshot = GameCatalogLoader.Load();

        var byRarity = snapshot.UnlockShardCostViews.ToDictionary(cost => cost.Rarity, cost => cost.Shards);

        Assert.Equal(
            new Dictionary<string, int>
            {
                ["Common"] = 40,
                ["Uncommon"] = 80,
                ["Rare"] = 130,
                ["Epic"] = 250,
                ["Legendary"] = 500,
                ["Mythic"] = 1400,
            },
            byRarity);
    }
}
