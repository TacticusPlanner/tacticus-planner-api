using System.Text.Json;
using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class NpcEncounterDenormalizerTests
{
    private static readonly Lazy<GameCatalogSnapshot> Catalog = new(GameCatalogLoader.Load);

    private static GameCatalogSnapshot Snapshot => Catalog.Value;

    private static IReadOnlyList<GameCatalogNpcEncounter> Encounters => Snapshot.NpcEncounterViews;

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static GameCatalogNpcEncounter Record(string npcId, string source, string groupId) =>
        Encounters.Single(record => record.NpcId == npcId && record.Source == source && record.GroupId == groupId);

    private static readonly GameCatalogRaidBossesView NoRaidBosses = new([], [], [], new Dictionary<string, GameCatalogRaidBossSeasonView>());

    // Only the given Survival / Onslaught / raid-boss inputs; every served view empty.
    private static NpcEncounterSourceData Data(
        IReadOnlyList<GameCatalogRawSurvivalSeason>? survival = null,
        IReadOnlyList<DatamineOnslaughtWave>? onslaught = null,
        GameCatalogRaidBossesView? raidBosses = null) =>
        new(
            Snapshot.NpcList,
            new HashSet<string>(["astraPrimarisPsy"], StringComparer.Ordinal),
            [],
            [],
            [],
            [],
            [],
            raidBosses ?? NoRaidBosses,
            new Dictionary<string, GameCatalogRaidBossRawUnitSet>(),
            survival ?? [],
            onslaught ?? []);

    [Fact]
    public void ABattleWritesEveryFieldIncludingNullsAndEmptyRounds()
    {
        var json = JsonSerializer.Serialize(
            new GameCatalogNpcEncounter("npcA", NpcEncounterSources.QuestEvent, "g",
                [new GameCatalogNpcEncounterBattle(null, null, null, 1, false, NpcEncounterRoles.Enemy, [], null, [])]),
            WebJson);

        Assert.Equal(
            """{"npcId":"npcA","source":"questEvent","groupId":"g","battles":[{"battleId":null,"track":null,"tier":null,"number":1,"challenge":false,"role":"enemy","rounds":[],"count":null,"levels":[]}]}""",
            json);
    }

    [Fact]
    public void ASurvivalSeasonServesTheRoundsTheNpcSpawnsIn()
    {
        var record = Record("blackNpc3TraitorGuardsman", NpcEncounterSources.Survival, "survival_crescendo_01");

        Assert.Equal([1, 3, 5, 12, 18, 22, 28, 34], record.Battles.SelectMany(battle => battle.Rounds));
        Assert.All(record.Battles, battle =>
        {
            Assert.Single(battle.Rounds);
            Assert.True(battle.Count > 0);
            Assert.Null(battle.BattleId);
            Assert.Null(battle.Tier);
        });
    }

    [Fact]
    public void ASurvivalRoundCountsTheRepeatsOfTheNpcInItsArmy()
    {
        var waves = new[]
        {
            new DatamineBattleSetWave(1, ["necroNpc1Warrior:1", "necroNpc1Warrior:1", "necroNpc1Warrior:2"]),
            new DatamineBattleSetWave(2, ["necroNpc1Warrior:3"]),
        };

        var record = Assert.Single(GameCatalogDenormalizer.BuildNpcEncounters(Data(survival: [new("survival_x", waves)])));

        Assert.Equal([3, 1], record.Battles.Select(battle => battle.Count));
    }

    [Fact]
    public void AnOnslaughtTierCountsItsLargestWaveNotTheirSum()
    {
        var waves = new[]
        {
            new DatamineOnslaughtWave(1, 1, null, ["necroNpc1Warrior:1", "necroNpc1Warrior:1"]),
            new DatamineOnslaughtWave(1, 1, null, ["necroNpc1Warrior:1"]),
            new DatamineOnslaughtWave(1, 2, null, ["necroNpc1Warrior:1"]),
        };

        var record = Assert.Single(GameCatalogDenormalizer.BuildNpcEncounters(Data(onslaught: waves)));
        var battle = Assert.Single(record.Battles);

        Assert.Equal(2, battle.Count);
        Assert.Equal([1, 2], battle.Rounds);
    }

    [Fact]
    public void ALegendaryEventBattleCountsTheNpcAcrossItsWaves()
    {
        var battles = Snapshot.LreBattleViews.ToDictionary(battle => battle.Id, StringComparer.Ordinal);

        Assert.All(
            Encounters.Where(encounter => encounter.Source == NpcEncounterSources.LegendaryEvent),
            encounter => Assert.All(encounter.Battles, battle => Assert.Equal(
                battles[battle.BattleId!].Waves
                    .SelectMany(wave => wave.Enemies)
                    .Where(enemy => enemy.Id == encounter.NpcId)
                    .Sum(enemy => enemy.Count),
                battle.Count)));
    }

    [Fact]
    public void AMixedLevelBattleSplitsItsCountByLevelLowestFirst()
    {
        var battle = Record("orksNpc1Grot", NpcEncounterSources.LegendaryEvent, "astarLysander")
            .Battles.Single(battle => battle.BattleId == "astarLysander-alpha-1");

        Assert.Equal([3, 4], battle.Levels.Select(level => level.ServedIndex));
        Assert.Equal(battle.Count, battle.Levels.Sum(level => level.Count));
    }

    // Rows checked against the game: with exactly these rows the app's power formula reproduces each battle's served
    // enemyPower (OE07 42419, IE08 19415). One row higher, OE07 comes to 53050.
    [Theory]
    [InlineData("OE07", "orksNpc1Grot:23,orksNpc2OrkBoy:23,templNpc1Initiate:15,templNpc2Neophyte:16,templNpc3InitiatePyreblaster:16,templNpc4Aggressor:13")]
    [InlineData("IE08", "necroBossC1Warden:1,necroNpc1Warrior:9,necroNpc2FlayedOne:9,necroNpc4Destroyer:8,necroNpc5Swarm:9")]
    public void CampaignEnemiesServeTheRowsThatReproduceTheBattlesPower(string battleId, string expected)
    {
        var battle = Snapshot.CampaignBattleViews.Single(view => view.Id == battleId);

        Assert.Equal(expected, string.Join(",", battle.DetailedEnemyTypes.Select(enemy => $"{enemy.Id}:{enemy.ServedIndex}")));
    }

    [Fact]
    public void EveryCampaignEnemyServesTheStarsOfItsOwnRow()
    {
        var npcs = Snapshot.NpcList.ToDictionary(npc => npc.Id, StringComparer.Ordinal);

        Assert.All(
            Snapshot.CampaignBattleViews.SelectMany(battle => battle.DetailedEnemyTypes),
            enemy => Assert.Equal(enemy.Stars, npcs[enemy.Id].Stats[enemy.ServedIndex].Stars));
    }

    [Fact]
    public void EveryCountedBattleSplitsItsCountIntoLevels() =>
        Assert.All(
            Encounters
                .Where(encounter => encounter.Source is not (NpcEncounterSources.GuildRaid or NpcEncounterSources.Onslaught))
                .SelectMany(encounter => encounter.Battles),
            battle => Assert.Equal(battle.Count, battle.Levels.Sum(level => level.Count)));

    [Fact]
    public void ASurvivalIndexIsOneBasedAndOnePastTheLadderHasNoLevel()
    {
        var waves = new[]
        {
            new DatamineBattleSetWave(1, ["necroNpc1Warrior:1", "necroNpc1Warrior:1", "necroNpc1Warrior:2", "necroNpc1Warrior:9999"]),
        };

        var record = Assert.Single(GameCatalogDenormalizer.BuildNpcEncounters(Data(survival: [new("survival_x", waves)])));

        Assert.Equal(
            [new GameCatalogNpcEncounterLevel(0, 2), new GameCatalogNpcEncounterLevel(1, 1), new GameCatalogNpcEncounterLevel(null, 1)],
            Assert.Single(record.Battles).Levels);
    }

    [Fact]
    public void AnOnslaughtLevelCountsItsLargestWave()
    {
        var waves = new[]
        {
            new DatamineOnslaughtWave(1, 1, null, ["necroNpc1Warrior:1", "necroNpc1Warrior:1"]),
            new DatamineOnslaughtWave(1, 1, null, ["necroNpc1Warrior:1", "necroNpc1Warrior:2"]),
        };

        var battle = Assert.Single(Assert.Single(GameCatalogDenormalizer.BuildNpcEncounters(Data(onslaught: waves))).Battles);

        Assert.Equal([new GameCatalogNpcEncounterLevel(0, 2), new GameCatalogNpcEncounterLevel(1, 1)], battle.Levels);
    }

    [Fact]
    public void AGuildRaidBattleHasNoLevels() =>
        Assert.All(
            Encounters.Where(encounter => encounter.Source == NpcEncounterSources.GuildRaid).SelectMany(encounter => encounter.Battles),
            battle => Assert.Empty(battle.Levels));

    [Fact]
    public void AGuildRaidBattleHasNoCount() =>
        Assert.All(
            Encounters.Where(encounter => encounter.Source == NpcEncounterSources.GuildRaid).SelectMany(encounter => encounter.Battles),
            battle => Assert.Null(battle.Count));

    [Fact]
    public void OnslaughtServesOneBattlePerTierItCanAppearIn()
    {
        var record = Record("tyranNpc1Hormagaunt", NpcEncounterSources.Onslaught, NpcEncounterSources.OnslaughtGroupId);

        Assert.Equal(Enumerable.Range(1, 21).Cast<int?>(), record.Battles.Select(battle => battle.Tier));
        Assert.Contains(1, record.Battles[0].Rounds);
        Assert.All(record.Battles, battle => Assert.Equal(battle.Rounds.Order(), battle.Rounds));
    }

    [Fact]
    public void AStorylineCampaignBattleIsServedWithItsNodeAndNoTrack()
    {
        var eventGroups = Snapshot.CampaignDefinitionViews.Where(group => group.ReleaseType == "event").Select(group => group.GroupId).ToHashSet();
        var battle = Snapshot.CampaignBattleViews.First(view => !eventGroups.Contains(view.CampaignGroupId) && view.DetailedEnemyTypes.Count > 0);
        var npcId = battle.DetailedEnemyTypes[0].Id;

        var entry = Assert.Single(Record(npcId, NpcEncounterSources.Campaign, battle.CampaignGroupId).Battles, b => b.BattleId == battle.Id);

        Assert.Null(entry.Track);
        Assert.Equal(battle.NodeNumber, entry.Number);
        Assert.Equal(battle.Challenge, entry.Challenge);
    }

    [Fact]
    public void AnEventCampaignChallengeBattleCarriesItsTrack()
    {
        var eventGroups = Snapshot.CampaignDefinitionViews.Where(group => group.ReleaseType == "event").Select(group => group.GroupId).ToHashSet();
        var battle = Snapshot.CampaignBattleViews.First(view =>
            eventGroups.Contains(view.CampaignGroupId) && view.Type == "Extremis" && view.Challenge && view.DetailedEnemyTypes.Count > 0);
        var npcId = battle.DetailedEnemyTypes[0].Id;

        var entry = Assert.Single(Record(npcId, NpcEncounterSources.CampaignEvent, battle.CampaignGroupId).Battles, b => b.BattleId == battle.Id);

        Assert.Equal("Extremis", entry.Track);
        Assert.Equal(battle.NodeNumber, entry.Number);
        Assert.True(entry.Challenge);
        Assert.DoesNotContain(Encounters, record => record.Source == NpcEncounterSources.Campaign && record.GroupId == battle.CampaignGroupId);
    }

    [Fact]
    public void ALegendaryEventBattleCarriesItsTrackAndNumber()
    {
        var record = Record("orksNpc1Grot", NpcEncounterSources.LegendaryEvent, "astarLysander");

        Assert.Contains(
            new GameCatalogNpcEncounterBattle("astarLysander-alpha-1", "alpha", null, 1, false, NpcEncounterRoles.Enemy, [], 23, []),
            record.Battles,
            BattleComparer.Instance);
    }

    [Fact]
    public void AQuestEventBattleIsServedByNumber()
    {
        var record = Record("blackNpc3TraitorGuardsman", NpcEncounterSources.QuestEvent, "astarEradicator");

        var first = record.Battles[0];
        Assert.Equal(1, first.Number);
        Assert.Null(first.BattleId);
    }

    [Fact]
    public void QuestBoardPowerUpsAreNotIndexed()
    {
        var objectIds = Snapshot.NpcList.Where(npc => npc.Kind == GameCatalogDenormalizer.NpcKindObject).Select(npc => npc.Id).ToHashSet();
        Assert.Contains(
            Snapshot.QuestEventViews.SelectMany(quest => quest.Battles).SelectMany(battle => battle.Enemies),
            enemy => objectIds.Contains(enemy.Id));

        Assert.DoesNotContain(Encounters, record => objectIds.Contains(record.NpcId));
    }

    [Fact]
    public void AnIncursionBattleCarriesItsTierAndBattleId()
    {
        var incursion = Snapshot.IncursionViews[0];
        var tier = incursion.Tiers[^1];
        var battle = tier.Battles.First(view => view.Enemies.Count > 0);

        var entry = Assert.Single(
            Record(battle.Enemies[0].NpcId, NpcEncounterSources.Incursion, incursion.MowId).Battles,
            b => b.BattleId == battle.Id && b.Tier == tier.Index);

        Assert.Equal(battle.Challenge, entry.Challenge);
        Assert.Null(entry.Number);
    }

    [Fact]
    public void AGuildRaidFieldUnitIsServedUnderTheBossItFightsAlongside()
    {
        var record = Record("tyranNpc3Termagant", NpcEncounterSources.GuildRaid, "GuildBoss1Boss1TyranTervigonLeviathan");

        var battle = Assert.Single(record.Battles);
        Assert.Equal(NpcEncounterRoles.Enemy, battle.Role);
    }

    [Fact]
    public void AHeroBasedPrimeIsNotIndexed()
    {
        Assert.Contains(Snapshot.RaidBossesView.Primes, prime => prime.QuestUnitId == "astraPrimarisPsy");

        Assert.DoesNotContain(Encounters, record => record.NpcId == "astraPrimarisPsy");
    }

    [Fact]
    public void SyncPvpVariationsAreNotIndexed() =>
        Assert.DoesNotContain(Encounters, record => record.NpcId.Contains("_SyncPvp_", StringComparison.Ordinal));

    [Fact]
    public void RecordsAreUniqueOrderedAndHoldNoDuplicateBattle()
    {
        var sourceOrder = NpcEncounterSources.Ordered.ToList();
        var keys = Encounters.Select(record => (record.NpcId, sourceOrder.IndexOf(record.Source), record.GroupId)).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(keys, key => Assert.True(key.Item2 >= 0));
        Assert.Equal(
            keys,
            keys.OrderBy(key => key.NpcId, StringComparer.Ordinal).ThenBy(key => key.Item2).ThenBy(key => key.GroupId, StringComparer.Ordinal));
        Assert.All(Encounters, record =>
        {
            Assert.NotEmpty(record.Battles);
            Assert.Equal(record.Battles.Count, record.Battles.Distinct(BattleComparer.Instance).Count());
        });
    }

    [Fact]
    public void AnNpcFoughtTwiceInOneGroupIsOneRecord()
    {
        var waves = new[]
        {
            new DatamineBattleSetWave(1, ["necroNpc1Warrior:1", "necroNpc1Warrior:2"]),
            new DatamineBattleSetWave(4, ["necroNpc1Warrior:3"]),
        };

        var record = Assert.Single(GameCatalogDenormalizer.BuildNpcEncounters(Data(survival: [new("survival_x", waves)])));

        Assert.Equal([1, 4], record.Battles.SelectMany(battle => battle.Rounds));
    }

    [Fact]
    public void TwoLoadsServeTheSameHash() =>
        Assert.Equal(
            GameCatalogLoader.Load().DatasetHashes[GameCatalogDatasets.NpcEncounters],
            Snapshot.DatasetHashes[GameCatalogDatasets.NpcEncounters]);

    [Fact]
    public void AMalformedSurvivalEntryFailsNamingSurvival()
    {
        var error = Assert.Throws<InvalidOperationException>(() => GameCatalogDenormalizer.BuildNpcEncounters(
            Data(survival: [new("survival_x", [new DatamineBattleSetWave(1, ["necroNpc1Warrior"])])])));

        Assert.Contains("'npc-encounters' (survival)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownSurvivalEnemyFailsNamingIt()
    {
        var error = Assert.Throws<InvalidOperationException>(() => GameCatalogDenormalizer.BuildNpcEncounters(
            Data(survival: [new("survival_x", [new DatamineBattleSetWave(1, ["notAnNpc:3"])])])));

        Assert.Contains("'npc-encounters' (survival)", error.Message, StringComparison.Ordinal);
        Assert.Contains("notAnNpc", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMalformedOnslaughtEntryFailsNamingOnslaught()
    {
        var error = Assert.Throws<InvalidOperationException>(() => GameCatalogDenormalizer.BuildNpcEncounters(
            Data(onslaught: [new DatamineOnslaughtWave(1, 1, null, ["tyranNpc1Hormagaunt:x"])])));

        Assert.Contains("'npc-encounters' (onslaught)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AGuildRaidQuestUnitThatIsNeitherNpcNorCharacterFails()
    {
        var prime = Snapshot.RaidBossesView.Primes[0] with { QuestUnitId = "nobody" };

        var error = Assert.Throws<InvalidOperationException>(() => GameCatalogDenormalizer.BuildNpcEncounters(
            Data(raidBosses: NoRaidBosses with { Primes = [prime] })));

        Assert.Contains("'npc-encounters' (guildRaid)", error.Message, StringComparison.Ordinal);
        Assert.Contains("nobody", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSurvivalReaderReadsOnlySurvivalSetsInKeyOrder()
    {
        var battleSets = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """
            {
              "survival_b": [{ "waves": [{ "round": 2, "army": ["npcA:1"] }] }],
              "legendary_event_1_lane_1": [{ "waves": [{ "round": 1, "army": ["npcB:1"] }] }],
              "survival_a": [{ "waves": [{ "round": 1, "army": ["npcC:1"] }, { "round": 3, "army": [] }] }]
            }
            """)!;

        var seasons = GameCatalogDenormalizer.ReadDatamineSurvivalSeasons(battleSets, WebJson);

        Assert.Equal(["survival_a", "survival_b"], seasons.Select(season => season.Id));
        Assert.Equal([1, 3], seasons[0].Waves.Select(wave => wave.Round));
    }

    [Fact]
    public void AnEmptyDatasetFailsValidation()
    {
        var errors = GameCatalogValidator.Validate(Snapshot with { NpcEncounterViews = [] });

        Assert.Contains(errors, error => error.Dataset == GameCatalogDatasets.NpcEncounters && error.Code == "EmptyDataset");
    }

    [Fact]
    public void AnEncounterNamingAnUnservedBattleFailsValidation()
    {
        var bad = new GameCatalogNpcEncounter("orksNpc1Grot", NpcEncounterSources.LegendaryEvent, "astarLysander",
            [new GameCatalogNpcEncounterBattle("astarLysander-alpha-99", "alpha", null, 99, false, NpcEncounterRoles.Enemy, [], 1, [new GameCatalogNpcEncounterLevel(0, 1)])]);

        var errors = GameCatalogValidator.Validate(Snapshot with { NpcEncounterViews = [.. Encounters, bad] });

        Assert.Contains(errors, error => error.Code == "InvalidNpcEncounter" && error.Message.Contains("astarLysander-alpha-99", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEncounterBattleWithoutACountFailsValidation()
    {
        var bad = new GameCatalogNpcEncounter("orksNpc1Grot", NpcEncounterSources.Onslaught, NpcEncounterSources.OnslaughtGroupId,
            [new GameCatalogNpcEncounterBattle(null, null, 1, null, false, NpcEncounterRoles.Enemy, [1], null, [])]);

        var errors = GameCatalogValidator.Validate(Snapshot with { NpcEncounterViews = [.. Encounters, bad] });

        Assert.Contains(errors, error => error.Code == "InvalidNpcEncounter" && error.Message.Contains("positive count", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEncounterBattleWithoutLevelsFailsValidation()
    {
        var bad = new GameCatalogNpcEncounter("orksNpc1Grot", NpcEncounterSources.Onslaught, NpcEncounterSources.OnslaughtGroupId,
            [new GameCatalogNpcEncounterBattle(null, null, 1, null, false, NpcEncounterRoles.Enemy, [1], 1, [])]);

        var errors = GameCatalogValidator.Validate(Snapshot with { NpcEncounterViews = [.. Encounters, bad] });

        Assert.Contains(errors, error => error.Code == "InvalidNpcEncounter" && error.Message.Contains("valid levels", StringComparison.Ordinal));
    }

    [Fact]
    public void TheShippedEncountersValidate() =>
        Assert.DoesNotContain(GameCatalogValidator.Validate(Snapshot), error => error.Dataset == GameCatalogDatasets.NpcEncounters);

    private sealed class BattleComparer : IEqualityComparer<GameCatalogNpcEncounterBattle>
    {
        public static readonly BattleComparer Instance = new();

        public bool Equals(GameCatalogNpcEncounterBattle? x, GameCatalogNpcEncounterBattle? y) =>
            x is not null && y is not null
            && x.BattleId == y.BattleId && x.Track == y.Track && x.Tier == y.Tier && x.Number == y.Number
            && x.Challenge == y.Challenge && x.Role == y.Role && x.Count == y.Count && x.Rounds.SequenceEqual(y.Rounds);

        public int GetHashCode(GameCatalogNpcEncounterBattle obj) => HashCode.Combine(obj.BattleId, obj.Track, obj.Tier, obj.Number, obj.Role);
    }
}
