using System.Text.Json;
using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class HreDenormalizerTests
{
    private static readonly Lazy<GameCatalogSnapshot> Catalog = new(GameCatalogLoader.Load);

    private static GameCatalogSnapshot Snapshot => Catalog.Value;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static GameCatalogHre Weirdboy => Snapshot.HreViews.Single(hre => hre.Id == "hero_release_event_1");

    private static Dictionary<string, JsonElement> Json(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private static DatamineHeroReleaseEvent Event(string name = "hero_release_event_1", string questId = "hre1_q1") =>
        new(
            name,
            "Orks",
            ["PvP"],
            new DatamineHreUnlockableHero("orksWeirdboy", "Rare", "Legendary"),
            new DatamineHreScorePoints(1, 1, 1, 3, 11, [5000], null, [new DatamineHreMilestone(100, "hreCurrency:55")]),
            questId,
            new GameCatalogOhSoCloseOffer(new GameCatalogOhSoCloseOfferPrice(450, 100), new GameCatalogOhSoCloseOfferPrice(100, 100)));

    private static DatamineHreSources Sources(
        DatamineHeroReleaseEvent? hre = null,
        string quests = """{ "hre1": { "quests": [ { "name": "hre1_q1", "rewards": ["hreCurrency:100"], "successorQuests": ["hre1_bonus"], "tasks": [{ "name": "RaidBattles", "target": 5 }] }, { "name": "hre1_bonus", "rewards": ["summoningToken"], "tasks": [{ "name": "DealDamageWithHero", "target": 15000, "taskParameters": { "heroId": "orksWeirdboy" } }] } ] } }""",
        string chests = """{ "hre_phase_chest_1_00": [{ "rewards": ["hreCurrency:550", "gold:7000-8000", "hreShards:33"] }], "hre_progress_chest_01": [{ "rewards": ["gold:500-550", "hreShards:10-11"] }] }""",
        string products = """{ "p1": { "chestId": "hre_progress_chest_01", "cost": { "type": "hreCurrency", "amount": 100 } }, "p2": { "chestId": "other" } }""") =>
        new(
            [hre ?? Event()],
            Json(quests),
            new DatamineLeaderboards(new Dictionary<string, DatamineLeaderboard>
            {
                ["heroReleaseLeaderboard"] = new(
                    [new DatamineLeaderboardPositionReward(0, "hreCurrency:100")],
                    [new DatamineLeaderboardSchedule(new DatamineLeaderboardOffset(7), [new DatamineLeaderboardTier(0, "hre_phase_chest_1_00")])]),
            }),
            Json(chests),
            Json(products));

    [Fact]
    public void TheShippedEventsAreTheGames()
    {
        Assert.Equal(["hero_release_event_1", "hero_release_event_2"], Snapshot.HreViews.Select(hre => hre.Id));
        Assert.Equal(("orksWeirdboy", "Rare", "Legendary", "Orks"), (Weirdboy.HeroId, Weirdboy.BaseRarity, Weirdboy.MaxRarity, Weirdboy.FeaturedFaction));
        Assert.Equal(3, Weirdboy.ScorePoints.CampaignScorePointsStaminaMultiplier);
        Assert.Equal([1, 4, 7, 11, 15, 20], Weirdboy.ScorePoints.GuildBossScorePointsDamageTableByRarity["Common"]);
        Assert.Equal(new GameCatalogOhSoCloseOffer(new(450, 100), new(100, 100)), Weirdboy.OhSoCloseOffer);
    }

    [Fact]
    public void TheShippedMilestonesMatchTheWikiLadder()
    {
        Assert.Equal(36, Weirdboy.Milestones.Count);
        Assert.Equal(new GameCatalogHreMilestone(100, 55), Weirdboy.Milestones[0]);
        Assert.Equal(new GameCatalogHreMilestone(1_000_000, 265), Weirdboy.Milestones[^1]);
        Assert.Equal(6090, Weirdboy.Milestones.Sum(milestone => milestone.Currency));
    }

    [Fact]
    public void TheShippedMissionsFollowTheChainEndingInTheBonus()
    {
        var missions = Weirdboy.Missions;

        Assert.Equal(21, missions.Count);
        Assert.Equal(Enumerable.Range(1, 21), missions.Select(mission => mission.Index));
        Assert.Equal("hre1_q1", missions[0].Id);
        Assert.Equal(2000, missions.Sum(mission => mission.Currency));
        Assert.Equal(("hre1_bonus", true, 0), (missions[^1].Id, missions[^1].Bonus, missions[^1].Currency));
        Assert.DoesNotContain(missions.SkipLast(1), mission => mission.Bonus);
        var task = missions[1].Tasks[1];
        Assert.Equal(("SlayEnemiesWithFactionTrait", 10, "Xenos"), (task.Name, task.Target, task.Parameters["factionTrait"]));
    }

    [Fact]
    public void TheShippedLeaderboardMatchesTheWikiBrackets()
    {
        var leaderboard = Weirdboy.Leaderboard;

        Assert.Equal("heroReleaseLeaderboard", leaderboard.Id);
        Assert.Equal("heroReleaseLeaderboard_2", Snapshot.HreViews[1].Leaderboard.Id);
        Assert.Equal(25, leaderboard.DailyRewards.Count);
        Assert.Equal(new GameCatalogHreDailyReward(0, 100), leaderboard.DailyRewards[0]);
        Assert.Equal(new GameCatalogHreDailyReward(200_000, 20), leaderboard.DailyRewards[^1]);
        Assert.Equal([7, (int?)null], leaderboard.Phases.Select(phase => phase.EndOffsetDays));
        Assert.Equal(new GameCatalogHrePhaseChest(0, "hre_phase_chest_1_00", 550, 33), leaderboard.Phases[0].Tiers[0]);
        Assert.Equal(new GameCatalogHrePhaseChest(200_000, "hre_phase_chest_2_240", 220, 6), leaderboard.Phases[1].Tiers[^1]);
    }

    [Fact]
    public void TheShippedChestShopMatchesTheWikiChests()
    {
        var shop = Weirdboy.ChestShop;

        Assert.Equal(28, shop.Count);
        Assert.Equal(new GameCatalogHreChest("hre_progress_chest_01", 100, 10, 11), shop[0]);
        Assert.Equal(new GameCatalogHreChest("hre_progress_chest_28", 5000, 40, 41), shop[^1]);
    }

    [Fact]
    public void ASmallEventReadsEveryPart()
    {
        var hre = Assert.Single(GameCatalogDenormalizer.ReadDatamineHres(Sources(), Options));

        Assert.Equal(["hre1_q1", "hre1_bonus"], hre.Missions.Select(mission => mission.Id));
        Assert.Equal((100, false), (hre.Missions[0].Currency, hre.Missions[0].Bonus));
        Assert.Equal(new GameCatalogHrePhaseChest(0, "hre_phase_chest_1_00", 550, 33), hre.Leaderboard.Phases[0].Tiers[0]);
        Assert.Equal([new GameCatalogHreChest("hre_progress_chest_01", 100, 10, 11)], hre.ChestShop);
    }

    public static TheoryData<string, DatamineHreSources> InvalidSources() => new()
    {
        { "event name must be hero_release_event_N", Sources(Event(name: "hre")) },
        { "quest group 'hre9' is missing", Sources(Event(questId: "hre9_q1")) },
        { "quest 'hre1_q2' is missing", Sources(quests: """{ "hre1": { "quests": [{ "name": "hre1_q1", "successorQuests": ["hre1_q2"], "tasks": [{ "name": "RaidBattles", "target": 5 }] }] } }""") },
        { "leaderboard 'heroReleaseLeaderboard_2' is missing", Sources(Event(name: "hero_release_event_2")) },
        { "chest 'hre_phase_chest_1_00' is missing", Sources(chests: """{ "hre_progress_chest_01": [{ "rewards": ["hreShards:10-11"] }] }""") },
        { "must cost a positive hreCurrency amount", Sources(products: """{ "p1": { "chestId": "hre_progress_chest_01", "cost": { "type": "gems", "amount": 100 } } }""") },
        { "reward 'hreShards:x' is not hreShards:N", Sources(chests: """{ "hre_phase_chest_1_00": [{ "rewards": ["hreShards:x"] }], "hre_progress_chest_01": [{ "rewards": ["hreShards:10"] }] }""") },
        { "milestone reward 'gold:5' is not hreCurrency:N", Sources(Event() with { ScorePoints = Event().ScorePoints! with { Rewards = [new DatamineHreMilestone(100, "gold:5")] } }) },
    };

    [Theory]
    [MemberData(nameof(InvalidSources))]
    public void AnUnmappableSourceFailsTheLoad(string expected, DatamineHreSources sources)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GameCatalogDenormalizer.ReadDatamineHres(sources, Options));

        Assert.Contains("'hre'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShippedEventsResolveTheirReferences()
    {
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateHres(Snapshot.HreViews, Snapshot.CharacterViews, Snapshot.FactionViews, Snapshot.DamageTypeViews, errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void UnknownReferencesFailValidation()
    {
        var task = new GameCatalogHreMissionTask("SlayEnemiesOfFaction", 1, new Dictionary<string, string> { ["factionId"] = "Squats" });
        var hre = Weirdboy with
        {
            HeroId = "nobody",
            Missions = [Weirdboy.Missions[0] with { Tasks = [task] }],
        };
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateHres([hre], Snapshot.CharacterViews, Snapshot.FactionViews, Snapshot.DamageTypeViews, errors);

        Assert.All(errors, error => Assert.Equal("InvalidHre", error.Code));
        Assert.Contains(errors, error => error.Message.Contains("unlocks 'nobody'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Message.Contains("unknown factionId 'Squats'", StringComparison.Ordinal));
    }
}
