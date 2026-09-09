using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class RaidBossDenormalizerTests
{
    private static GameCatalogRaidBossRawStat Stat(int index, int health = 1000) =>
        new(health, 10, 5, index, 1, "Common", index, index + 1, null, null, null, null, null);

    private static GameCatalogRaidBossRawUnitSet UnitSet(
        string faction = "Tyranids",
        int movement = 4,
        IReadOnlyList<GameCatalogRaidBossRawStat>? stats = null,
        IReadOnlyList<GameCatalogRaidBossRawWeapon>? weapons = null,
        IReadOnlyList<string>? active = null,
        IReadOnlyList<string>? traits = null,
        string? questUnitId = null) =>
        new(faction, movement, null, questUnitId, stats ?? [Stat(0), Stat(1)], weapons, active, null, null, traits);

    private static GameCatalogRaidBossRawData Data(
        IReadOnlyDictionary<string, GameCatalogRaidBossRawUnitSet> unitSets,
        IReadOnlyDictionary<string, GameCatalogRaidBossRawSeason>? seasons = null,
        IReadOnlyDictionary<string, GameCatalogRaidBossRawModifier>? modifiers = null,
        IReadOnlyList<string>? rotation = null,
        IReadOnlyList<string>? primarchs = null) =>
        new(
            rotation ?? ["season-1"],
            primarchs ?? [],
            unitSets,
            seasons ?? new Dictionary<string, GameCatalogRaidBossRawSeason>(),
            modifiers ?? new Dictionary<string, GameCatalogRaidBossRawModifier>());

    [Fact]
    public void ClassifiesBossesAndPrimesFromTheKeyPatternAndOrdersThem()
    {
        var view = GameCatalogDenormalizer.BuildRaidBosses(Data(new Dictionary<string, GameCatalogRaidBossRawUnitSet>
        {
            ["GuildBoss2Boss1TyranHiveTyrant"] = UnitSet(),
            ["GuildBoss1Boss1TyranTervigon"] = UnitSet(),
            ["GuildBoss1MiniBoss2TyranWarriorB"] = UnitSet(),
            ["GuildBoss1MiniBoss1TyranWarriorA"] = UnitSet(),
            ["GuildBoss2Npc1TyranTermagant"] = UnitSet(),
            ["GuildBoss1LootObjCrate"] = UnitSet(),
        }));

        Assert.Equal(
            ["GuildBoss1Boss1TyranTervigon", "GuildBoss2Boss1TyranHiveTyrant"],
            view.Bosses.Select(boss => boss.UnitSetId));
        Assert.All(view.Bosses, boss => Assert.Equal("boss", boss.Kind));

        Assert.Equal(
            ["GuildBoss1MiniBoss1TyranWarriorA", "GuildBoss1MiniBoss2TyranWarriorB"],
            view.Primes.Select(prime => prime.UnitSetId));
        Assert.All(view.Primes, prime => Assert.Equal("prime", prime.Kind));
    }

    [Fact]
    public void MarksPrimarchsFromTheRawList()
    {
        var view = GameCatalogDenormalizer.BuildRaidBosses(Data(
            new Dictionary<string, GameCatalogRaidBossRawUnitSet>
            {
                ["GuildBoss5Boss1DeathMortarion"] = UnitSet(),
                ["GuildBoss1MiniBoss1TyranWarrior"] = UnitSet(),
            },
            primarchs: ["GuildBoss5Boss1DeathMortarion"]));

        Assert.True(view.Bosses.Single().IsPrimarch);
        Assert.False(view.Primes.Single().IsPrimarch);
    }

    [Fact]
    public void ProgressionLadderRoundTripsInOrderAndWeaponsKeepRangeOnlyWhenRanged()
    {
        var view = GameCatalogDenormalizer.BuildRaidBosses(Data(new Dictionary<string, GameCatalogRaidBossRawUnitSet>
        {
            ["GuildBoss1Boss1Tyran"] = UnitSet(
                stats: [Stat(0, 1000), Stat(1, 2000), Stat(2, 3000)],
                weapons:
                [
                    new GameCatalogRaidBossRawWeapon(3, "melee-profile", null),
                    new GameCatalogRaidBossRawWeapon(1, "ranged-profile", 4),
                ]),
        }));

        var boss = view.Bosses.Single();
        Assert.Equal([1000, 2000, 3000], boss.StatProgression.Select(step => step.Health));
        Assert.NotNull(boss.Weapons);
        Assert.Null(boss.Weapons![0].Range);
        Assert.Equal(4, boss.Weapons[1].Range);
    }

    [Fact]
    public void EmptyAbilityAndWeaponCollectionsAreOmittedFromThePayload()
    {
        var view = GameCatalogDenormalizer.BuildRaidBosses(Data(new Dictionary<string, GameCatalogRaidBossRawUnitSet>
        {
            ["GuildBoss1Boss1Tyran"] = UnitSet(weapons: null, active: [], traits: ["trait-a"]),
        }));

        var boss = view.Bosses.Single();
        Assert.Null(boss.Weapons);
        Assert.Null(boss.ActiveAbilityIds);
        Assert.Equal(["trait-a"], boss.TraitIds);
    }

    [Fact]
    public void QuestUnitIdPassesThroughWhenPresentAndIsOmittedWhenAbsent()
    {
        var view = GameCatalogDenormalizer.BuildRaidBosses(Data(new Dictionary<string, GameCatalogRaidBossRawUnitSet>
        {
            ["GuildBoss1Boss1TyranTervigon"] = UnitSet(questUnitId: "tyranNpc3Termagant"),
            ["GuildBoss1MiniBoss1TyranWarrior"] = UnitSet(),
        }));

        Assert.Equal("tyranNpc3Termagant", view.Bosses.Single().QuestUnitId);
        Assert.Null(view.Primes.Single().QuestUnitId);
    }

    [Fact]
    public void EncounterSplitsTheProgressionSuffixAndUnionsFieldNpcIds()
    {
        var season = new GameCatalogRaidBossRawSeason("season-1", null, null,
        [
            new GameCatalogRaidBossRawTier(6,
            [
                new GameCatalogRaidBossRawSet(0, "chest-0", 100,
                [
                    new GameCatalogRaidBossRawEncounter(
                        0, "Boss", "GB_01", 6, "TervigonLeviathan",
                        "GuildBoss1Boss1Tyran:3",
                        "GuildBoss1Npc1Termagant:1", null,
                        ["GuildBoss1Npc1Termagant:1", "GuildBoss1Npc2Hormagaunt:2"],
                        ["Tyranids"],
                        [new GameCatalogRaidBossRawEncounterModifier(25, "mod-a")]),
                    new GameCatalogRaidBossRawEncounter(
                        1, "Crystal", "GB_02", 4, null, "GuildBoss1Boss1Tyran", null, null, null, null, null),
                ]),
            ]),
        ]);

        var view = GameCatalogDenormalizer.BuildRaidBosses(Data(
            new Dictionary<string, GameCatalogRaidBossRawUnitSet> { ["GuildBoss1Boss1Tyran"] = UnitSet() },
            seasons: new Dictionary<string, GameCatalogRaidBossRawSeason> { ["season-1"] = season },
            modifiers: new Dictionary<string, GameCatalogRaidBossRawModifier>
            {
                ["mod-a"] = new("bossStatDecrease", "movement", null, 1),
            }));

        var encounters = view.Seasons["season-1"].Tiers.Single().Sets.Single().Encounters;

        Assert.Equal("GuildBoss1Boss1Tyran", encounters[0].UnitSetId);
        Assert.Equal(3, encounters[0].ProgressionIndex);
        Assert.Equal(
            ["GuildBoss1Npc1Termagant", "GuildBoss1Npc2Hormagaunt"],
            encounters[0].FieldNpcIds);
        Assert.Equal(["Tyranids"], encounters[0].DisallowedFactionIds);

        Assert.Equal(1, encounters[1].ProgressionIndex);
        Assert.Empty(encounters[1].FieldNpcIds);
        Assert.Empty(encounters[1].Modifiers);
    }

    [Fact]
    public void EncounterModifierInlinesTheResolvedDefinition()
    {
        var season = new GameCatalogRaidBossRawSeason("season-1", null, null,
        [
            new GameCatalogRaidBossRawTier(6,
            [
                new GameCatalogRaidBossRawSet(0, "chest-0", 100,
                [
                    new GameCatalogRaidBossRawEncounter(
                        0, "Boss", "GB_01", 6, null, "GuildBoss1Boss1Tyran:1", null, null, null, null,
                        [new GameCatalogRaidBossRawEncounterModifier(180, "boss_debuff_movement_1")]),
                ]),
            ]),
        ]);

        var view = GameCatalogDenormalizer.BuildRaidBosses(Data(
            new Dictionary<string, GameCatalogRaidBossRawUnitSet> { ["GuildBoss1Boss1Tyran"] = UnitSet() },
            seasons: new Dictionary<string, GameCatalogRaidBossRawSeason> { ["season-1"] = season },
            modifiers: new Dictionary<string, GameCatalogRaidBossRawModifier>
            {
                ["boss_debuff_movement_1"] = new("bossStatDecrease", "movement", null, 1),
            }));

        var modifier = view.Seasons["season-1"].Tiers.Single().Sets.Single().Encounters.Single().Modifiers.Single();
        Assert.Equal(180, modifier.HpLost);
        Assert.Equal("boss_debuff_movement_1", modifier.ModifierId);
        Assert.Equal("bossStatDecrease", modifier.Type);
        Assert.Equal("movement", modifier.Target);
        Assert.Null(modifier.Subtarget);
        Assert.Equal(1, modifier.Amount);
    }

    [Fact]
    public void SeasonConfigRotationIsPreservedInOrder()
    {
        var view = GameCatalogDenormalizer.BuildRaidBosses(Data(
            new Dictionary<string, GameCatalogRaidBossRawUnitSet> { ["GuildBoss1Boss1Tyran"] = UnitSet() },
            rotation: ["c1", "c2", "c3", "c4", "c5"]));

        Assert.Equal(["c1", "c2", "c3", "c4", "c5"], view.SeasonConfigRotation);
    }
}
