using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

/// <summary>Covers <c>add-legendary-event-run-inputs</c>'s <c>legendary-event-reward-ladder</c> capability: each
/// served <c>lres</c> record carries its own reward ladder, malformed ladders fail validation, and no shared
/// <c>lre-common</c> dataset is served.</summary>
public sealed class LreDenormalizerTests
{
    private static readonly GameCatalogSnapshot Snapshot = GameCatalogLoader.Load();

    private static GameCatalogLreView View(string id) => Snapshot.LreViews.Single(view => view.Id == id);

    private static GameCatalogLre Raw(string id) => Snapshot.Lres.Single(lre => lre.UnitSnowprintId == id);

    [Fact]
    public void ServedRewardsEqualTheRawFile()
    {
        var rewards = View("votanUthar").Rewards;
        var raw = Raw("votanUthar");

        Assert.Equal(raw.PointsMilestones, rewards.PointsMilestones);
        Assert.Equal(raw.ChestsMilestones, rewards.ChestsMilestones);
        Assert.Equal(25, rewards.ShardsPerChest);
        Assert.Equal(new GameCatalogLreProgression(400, 120, 180, 200, 250, 150), rewards.Progression);
    }

    [Fact]
    public void EventsCarryTheirOwnChestLadders()
    {
        var lysander = View("astarLysander").Rewards.ChestsMilestones;
        var uthar = View("votanUthar").Rewards.ChestsMilestones;

        Assert.Equal((60, 44_450), (lysander.Count, lysander.Sum(chest => chest.EngramCost)));
        Assert.Equal((54, 35_450), (uthar.Count, uthar.Sum(chest => chest.EngramCost)));
        Assert.Equal(uthar, lysander.Take(54));
    }

    [Fact]
    public void TheShippedLaddersAreValid()
    {
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateLreRewards(Snapshot.Lres, errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void AChestLadderThatSkipsALevelFailsValidation()
    {
        var raw = Raw("votanUthar");
        var skipping = raw.ChestsMilestones.Where(chest => chest.ChestLevel != 4).ToList();
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateLreRewards([raw with { ChestsMilestones = skipping }], errors);

        var error = Assert.Single(errors);
        Assert.Equal((GameCatalogDatasets.LresPrefix, "InvalidRewardLadder"), (error.Dataset, error.Code));
        Assert.Contains("votanUthar", error.Message, StringComparison.Ordinal);
        Assert.Contains(raw.SourceFile, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PointsMilestonesMustAscend()
    {
        var raw = Raw("votanUthar");
        var points = raw.PointsMilestones.ToList();
        points[3] = points[3] with { CumulativePoints = points[2].CumulativePoints };
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateLreRewards([raw with { PointsMilestones = points }], errors);

        Assert.Single(errors);
    }

    [Fact]
    public void NonPositiveShardsPerChestOrProgressionFailValidation()
    {
        var raw = Raw("votanUthar");
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateLreRewards(
            [raw with { ShardsPerChest = 0 }, raw with { Progression = raw.Progression with { Mythic = 0 } }],
            errors);

        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void NoSharedLadderDatasetIsServed()
    {
        Assert.DoesNotContain("lre-common", GameCatalogDatasets.Served);
        Assert.DoesNotContain("lre-common", Snapshot.Manifest.Datasets.Select(dataset => dataset.Key));
        Assert.Contains(GameCatalogDatasets.Lres, Snapshot.Manifest.Datasets.Select(dataset => dataset.Key));
        Assert.Contains(GameCatalogDatasets.LreBattles, Snapshot.Manifest.Datasets.Select(dataset => dataset.Key));
    }
}
