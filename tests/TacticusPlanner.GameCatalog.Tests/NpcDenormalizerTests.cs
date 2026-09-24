using System.Text.Json;
using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class NpcDenormalizerTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static GameCatalogNpcStat Stat(int rank, int stars, int health = 100) =>
        new(1, 10, 5, health, 0, rank, stars);

    private static GameCatalogRawNpc Npc(
        string id,
        IReadOnlyList<string>? traits = null,
        IReadOnlyList<GameCatalogNpcStat>? stats = null,
        string? rangedDamage = null) =>
        new(id, id + " name", "Physical", 1, rangedDamage, rangedDamage is null ? null : 2, rangedDamage is null ? null : 3, 3,
            traits ?? [], [], [], [], [], stats ?? [Stat(0, 0)]);

    private static GameCatalogFactionNpcs Faction(string factionId, string alliance, params GameCatalogRawNpc[] npcs) =>
        new(alliance, factionId, factionId + " display", npcs);

    private static IReadOnlyList<GameCatalogNpc> Build(params (string key, GameCatalogFactionNpcs faction)[] files) =>
        GameCatalogDenormalizer.BuildNpcs(files.ToDictionary(file => file.key, file => file.faction, StringComparer.Ordinal));

    [Fact]
    public void StampsFactionAndAllianceFromTheOwningSourceFile()
    {
        var served = Build(
            ("npcs-necrons", Faction("Necrons", "Xenos", Npc("necroBossWarden"))),
            ("npcs-objects", Faction("Objects", "Neutral", Npc("LootObj_AmmoBox"))));

        var warden = Assert.Single(served, npc => npc.Id == "necroBossWarden");
        Assert.Equal("Necrons", warden.FactionId);
        Assert.Equal("Xenos", warden.Alliance);

        var ammoBox = Assert.Single(served, npc => npc.Id == "LootObj_AmmoBox");
        Assert.Equal("Objects", ammoBox.FactionId);
        Assert.Equal("Neutral", ammoBox.Alliance);
    }

    [Fact]
    public void ClassifiesObjectsBySourceFileRegardlessOfTraits()
    {
        var served = Build(("npcs-objects", Faction("Objects", "Neutral",
            Npc("LootObj_AmmoBox", traits: ["Object"]),
            Npc("LootObj_CoinCrate"))));

        Assert.All(served, npc => Assert.Equal(GameCatalogDenormalizer.NpcKindObject, npc.Kind));
    }

    [Fact]
    public void ClassifiesMachinesOfWarByTraitRegardlessOfIdCasing()
    {
        var served = Build(
            ("npcs-deathguard", Faction("DeathGuard", "Chaos", Npc("deathNpcMoWCrawler", traits: ["MachineOfWar", "Vehicle"]))),
            ("npcs-astramilitarum", Faction("AstraMilitarum", "Imperial", Npc("astraNpcMowOrdnanceBattery", traits: ["Vehicle", "MachineOfWar"]))));

        Assert.All(served, npc => Assert.Equal(GameCatalogDenormalizer.NpcKindMachineOfWar, npc.Kind));
    }

    [Fact]
    public void ClassifiesEverythingElseAsUnitIncludingZeroStatLadders()
    {
        var served = Build(("npcs-necrons", Faction("Necrons", "Xenos",
            Npc("necroNpc1Warrior", traits: ["LivingMetal", "Mechanical"]),
            Npc("necroNpcMoWLookalike"),
            Npc("genesDecoy", stats: [new GameCatalogNpcStat(1, 0, 0, 0, 0, 0, 0)]))));

        Assert.All(served, npc => Assert.Equal(GameCatalogDenormalizer.NpcKindUnit, npc.Kind));

        var decoy = Assert.Single(served, npc => npc.Id == "genesDecoy");
        var row = Assert.Single(decoy.Stats);
        Assert.Equal((0, 0, 0), (row.Health, row.Armour, row.Damage));
    }

    [Fact]
    public void OrdersBySourceKeyThenSourceOrderAndKeepsStatRowsAsAuthored()
    {
        var unsorted = new[] { Stat(2, 2, 160), Stat(1, 2, 140), Stat(9, 6, 1028) };
        var served = Build(
            ("npcs-necrons", Faction("Necrons", "Xenos", Npc("necroNpcWarden"), Npc("necroBossWarden", stats: unsorted))),
            ("npcs-aeldari", Faction("Aeldari", "Xenos", Npc("eldarNpc1Guardian"))));

        Assert.Equal(["eldarNpc1Guardian", "necroNpcWarden", "necroBossWarden"], served.Select(npc => npc.Id));

        var warden = served.Single(npc => npc.Id == "necroBossWarden");
        Assert.Equal(unsorted.Select(stat => (stat.Rank, stat.Stars, stat.Health)), warden.Stats.Select(stat => (stat.Rank, stat.Stars, stat.Health)));
    }

    [Fact]
    public void ServedRecordCarriesTheSpecifiedFieldsAndNoPresentationalOnes()
    {
        var served = Build(("npcs-necrons", Faction("Necrons", "Xenos", Npc("necroBossWarden", rangedDamage: "Gauss"))));
        var json = JsonSerializer.SerializeToElement(served[0], WebJson);

        string[] expected =
        [
            "id", "name", "factionId", "alliance", "kind", "meleeDamage", "meleeHits", "rangedDamage", "rangedHits",
            "distance", "movement", "traits", "activeAbilityDamage", "activeAbilities", "passiveAbilityDamage",
            "passiveAbilities", "stats",
        ];
        Assert.Equal(expected, json.EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("icon", json.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void RawRecordIgnoresTheIconPathOnDeserialize()
    {
        const string raw = """
            {"id":"necroBossWarden","name":"Makhotep","meleeDamage":"Physical","meleeHits":1,"rangedDamage":"Gauss",
             "rangedHits":2,"distance":3,"movement":3,"traits":["LivingMetal"],"activeAbilityDamage":[],"activeAbilities":[],
             "passiveAbilityDamage":[],"passiveAbilities":[],
             "icon":"snowprint_assets/characters/ui_image_portrait_necro_warden_01.png",
             "stats":[{"abilityLevel":5,"damage":34,"armour":38,"health":160,"progressionIndex":3,"rank":2,"stars":2}]}
            """;

        var npc = JsonSerializer.Deserialize<GameCatalogRawNpc>(raw, WebJson);

        Assert.NotNull(npc);
        Assert.Equal("necroBossWarden", npc.Id);
        Assert.Equal("Gauss", npc.RangedDamage);
    }
}
