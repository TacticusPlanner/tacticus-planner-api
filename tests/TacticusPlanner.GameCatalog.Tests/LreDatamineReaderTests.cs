using System.Text.Json;
using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class LreDatamineReaderTests
{
    private const string Unit = "lostaBile";

    private static readonly GameCatalogLreTrackCuration CuratedTrack = new(
        "Alpha (No Chaos)",
        new GameCatalogLreTrackEnemies("Black Legion", "https://tacticus.wiki.gg/wiki/Black_Legion#NPC_Units"),
        [31],
        [new GameCatalogLreFilter("Alliance", "Chaos", true)],
        ["Black Templars", "No Bolter", "Ranged"]);

    private static object Battle(params object[] objectives) => new
    {
        tier = 1,
        battleNr = 1,
        boardId = "LE1_02",
        power = 1184,
        waves = new[] { new { round = 1, power = 272, army = new[] { "thousNpc1PinkHorror:1", "blackNpc1Bloodletter:2", "thousNpc1PinkHorror:1" } } },
        objectives,
        disallowedFactions = new[] { "BlackLegion" },
    };

    private static readonly object[] Objectives =
    [
        new { objectiveType = "Acing", score = 37 },
        new { objectiveType = "Faction", objectiveTarget = "BlackTemplars", score = 100 },
        new { objectiveType = "NotDamageType", objectiveTarget = "Bolter", score = 30 },
        new { objectiveType = "HasRangedAttack", score = 45 },
    ];

    private static readonly string[] FirstChestRewards = ["gold:500-550", "legendaryEventShards:25"];
    private static readonly string[] SecondChestRewards = ["legendaryEventShards:25"];

    private static object Task(string name, int target, object? parameters = null) =>
        new { name, target, taskParameters = parameters };

    private static DatamineLreSources Sources(object[]? objectives = null, object? regularTask = null)
    {
        static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, JsonSerializerOptions.Web);

        var battleSet = Json(new[] { Battle(objectives ?? Objectives) });
        return new DatamineLreSources(
            [new DatamineLegendaryHeroEvent(
                "legendary_hero_event_16",
                "leg_hero_event_16",
                "legendaryHeroEvent16",
                ["leg_bile", "leg_bile_premium"],
                [new(1, "lane_1"), new(2, "lane_2"), new(3, "lane_3")],
                new DatamineLegendaryHeroUnlock(Unit))],
            new Dictionary<string, JsonElement> { ["lane_1"] = battleSet, ["lane_2"] = battleSet, ["lane_3"] = battleSet },
            new Dictionary<string, JsonElement>
            {
                ["leg_bile"] = Json(new
                {
                    questType = "standard",
                    quests = new[]
                    {
                        new { name = "leg_bile_01", tasks = new[] { Task("DefeatWaves", 5), Task("DealDamage", 1000) } },
                        new { name = "leg_bile_02", tasks = new[] { regularTask ?? Task("SlayEnemiesOfTrait", 30, new { trait = "Daemon" }) } },
                    },
                }),
                ["leg_bile_premium"] = Json(new
                {
                    questType = "premium",
                    quests = new[] { new { name = "leg_bile_premium_01", tasks = new[] { Task("DealAbilityDamage", 2500) } } },
                }),
            },
            new Dictionary<string, List<DatamineTieredProgressReward>>
            {
                ["leg_hero_event_16"] = [new(100, "legendaryEventCurrency:25", null, null), new(250, "legendaryEventCurrency:30", null, null)],
            },
            [new DatamineShopSegment("legendaryHeroEvent16", [new DatamineShopProductSet(["chest_01", "chest_02"])])],
            new Dictionary<string, JsonElement>
            {
                ["chest_01"] = Json(new { chestId = "leg_chest_01", cost = new { type = "legendaryEventCurrency", amount = 60 } }),
                ["chest_02"] = Json(new { chestId = "leg_chest_02", cost = new { type = "legendaryEventCurrency", amount = 80 } }),
            },
            new Dictionary<string, JsonElement>
            {
                ["leg_chest_01"] = Json(new[] { new { rewards = FirstChestRewards } }),
                ["leg_chest_02"] = Json(new[] { new { rewards = SecondChestRewards } }),
            },
            new Dictionary<string, List<DatamineUnitProgressionStep>>
            {
                [Unit] =
                [
                    new(8, "Legendary", new Dictionary<string, int> { ["legendaryEventShards"] = 400 }),
                    new(9, "Legendary", new Dictionary<string, int> { ["legendaryEventShards"] = 120 }),
                    new(10, "Legendary", new Dictionary<string, int> { ["legendaryEventShards"] = 180 }),
                    new(11, "Legendary", new Dictionary<string, int> { ["legendaryEventShards"] = 200 }),
                    new(11, "Mythic", new Dictionary<string, int> { ["legendaryEventShards"] = 250 }),
                    new(12, "Mythic", new Dictionary<string, int> { ["legendaryEventShards"] = 150 }),
                ],
            });
    }

    private static GameCatalogLre Read(DatamineLreSources sources) =>
        Assert.Single(GameCatalogDenormalizer.ReadDatamineLres(
            new Dictionary<string, GameCatalogLreCuration>
            {
                [Unit] = new("legendary_hero_event_16", "Fabius Bile", null, CuratedTrack, CuratedTrack, CuratedTrack),
            },
            sources,
            JsonSerializerOptions.Web)).Value;

    [Fact]
    public void AnEventReadsItsBattlesRestrictionsAndRewardsFromTheGame()
    {
        var lre = Read(Sources());

        Assert.Equal((16, 1, 3), (lre.Id, lre.BattlesCount, lre.ConstraintsCount));
        Assert.Equal(37, lre.Alpha.KillPoints);
        Assert.Equal([37], lre.Alpha.DefeatAll);
        Assert.Equal(
            [
                new GameCatalogLreRestriction("Black Templars", 100, 0, new("Faction", "BlackTemplars", false)),
                new GameCatalogLreRestriction("No Bolter", 30, 1, new("DamageType", "Bolter", true)),
                new GameCatalogLreRestriction("Ranged", 45, 2, new("AttackType", "Ranged", false)),
            ],
            lre.Alpha.UnitsRestrictions);
        Assert.Equal(
            [("thousNpc1PinkHorror", 1, 2), ("blackNpc1Bloodletter", 2, 1)],
            Assert.Single(Assert.Single(lre.Alpha.Battles).Waves).Enemies.Select(enemy => (enemy.Id, enemy.LadderIndex, enemy.Count)));
        Assert.Equal([new GameCatalogLrePointsMilestone(1, 100, 25), new GameCatalogLrePointsMilestone(2, 250, 30)], lre.PointsMilestones);
        Assert.Equal([new GameCatalogLreChestsMilestone(1, 60), new GameCatalogLreChestsMilestone(2, 80)], lre.ChestsMilestones);
        Assert.Equal(25, lre.ShardsPerChest);
        Assert.Equal(new GameCatalogLreProgression(400, 120, 180, 200, 250, 150), lre.Progression);
    }

    [Fact]
    public void MissionTasksBecomeV1MissionText()
    {
        var lre = Read(Sources());

        Assert.Equal(["Defeat 5 waves of enemies in any wave-based game mode, Deal 1000 damage", "Defeat 30 Daemon enemies"], lre.RegularMissions);
        Assert.Equal(["Deal 2.5k damage with Abilities"], lre.PremiumMissions);
    }

    [Fact]
    public void AnUnknownRestrictionTypeFails()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Read(Sources(
            [.. Objectives.Take(3), new { objectiveType = "NoMoreThanTwoUnits", score = 10 }])));

        Assert.Contains("lres", error.Message, StringComparison.Ordinal);
        Assert.Contains("'NoMoreThanTwoUnits'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissionTraitWithNoLabelFails()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Read(Sources(
            regularTask: Task("WinBattlesWithoutTrait", 5, new { trait = "BeastSnagga" }))));

        Assert.Contains("'BeastSnagga'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippedChaosTracksAlsoDisallowTheLostAndTheDamned()
    {
        var gamma = GameCatalogLoader.Load().LresByEvent["lres-votanuthar"].Gamma;

        Assert.All(gamma.Battles, battle => Assert.Contains("TheLostAndTheDamned", battle.DisallowedFactions));
    }
}
