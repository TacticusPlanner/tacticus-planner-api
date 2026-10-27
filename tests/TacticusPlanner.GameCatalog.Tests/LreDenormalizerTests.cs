using System.Text.Json;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

/// <summary>Covers <c>add-legendary-event-run-inputs</c>'s <c>legendary-event-reward-ladder</c> capability: each
/// served <c>lres</c> record carries its own reward ladder, malformed ladders fail validation, and no shared
/// <c>lre-common</c> dataset is served. Also covers <c>add-legendary-event-battle-preview</c>'s
/// <c>legendary-event-battles-dataset</c> capability: wave enemies carry a 1-based <c>ladderIndex</c>.</summary>
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
    public void EveryEventCarriesItsChestLadderFromTheGame()
    {
        // The game's shop segments give all four events the same 60 chests (V1 had cut Farsight's and Uthar's at 54).
        Assert.All(Snapshot.LreViews, view => Assert.Equal(
            (60, 44_450), (view.Rewards.ChestsMilestones.Count, view.Rewards.ChestsMilestones.Sum(chest => chest.EngramCost))));
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

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static GameCatalogLreBattleView Battle(string id) => Snapshot.LreBattleViews.Single(battle => battle.Id == id);

    [Fact]
    public void ServedWaveEnemiesCarryTheirLadderIndex()
    {
        var battle = Battle("astarLysander-alpha-1");

        Assert.Equal(("astarLysander", "alpha", "LE1_26", 1, 994), (battle.LreId, battle.Track, battle.MapId, battle.Number, battle.Power));
        var wave = battle.Waves[0];
        Assert.Equal((1, 147), (wave.Round, wave.Power));
        Assert.Equal([new GameCatalogLreEnemy("orksNpc1Grot", 4, 7)], wave.Enemies);
    }

    [Fact]
    public void EveryRawWaveEnemyHasALadderIndex()
    {
        // A raw enemy still keyed `stars` would deserialize with LadderIndex 0.
        var enemies = Snapshot.LreBattleViews.SelectMany(battle => battle.Waves).SelectMany(wave => wave.Enemies).ToList();

        Assert.Equal(3_048, enemies.Count);
        Assert.All(enemies, enemy => Assert.True(enemy.LadderIndex >= 1, $"{enemy.Id} has ladderIndex {enemy.LadderIndex}"));
    }

    [Fact]
    public void ServedBattlesSerializeLadderIndexAndNoStars()
    {
        var json = JsonSerializer.Serialize(Snapshot.LreBattleViews, WebJson);

        Assert.Contains("\"ladderIndex\":4", json);
        Assert.DoesNotContain("\"stars\"", json);
    }

    [Fact]
    public void ALadderIndexPastTheLadderIsServedAsIs()
    {
        var helbrecht = Battle("astarLysander-beta-13").Waves
            .SelectMany(wave => wave.Enemies)
            .Single(enemy => enemy.Id == "templBossHelbrecht");

        // Index 3 is one past the game's three-row ladder (0-2).
        Assert.Equal(3, helbrecht.LadderIndex);
        Assert.Equal(3, Snapshot.Npcs.Single(npc => npc.Id == "templBossHelbrecht").Stats.Count);
    }
}
