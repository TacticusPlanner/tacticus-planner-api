using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class GuildWarDenormalizerTests
{
    private static readonly IReadOnlyList<GameCatalogGuildWarSeason> Shipped = GameCatalogLoader.Load().GuildWarViews;

    private static GameCatalogGuildWarSeason Season(string id) => Shipped.Single(season => season.Id == id);

    [Fact]
    public void OnlyTheLiveSeasonsAreServed() =>
        Assert.Equal(
            ["guildWarSeasonA", "guildWarSeasonB", "guildWarSeasonC", "guildWarSeasonD"],
            Shipped.Select(season => season.Id));

    [Fact]
    public void SeasonsKeepTheirOwnZones()
    {
        var a = Season("guildWarSeasonA").Zones.Select(zone => zone.Id).ToArray();
        var d = Season("guildWarSeasonD").Zones.Select(zone => zone.Id).ToArray();

        Assert.Contains("LandingPad1", a);
        Assert.Contains("AntiAirBattery", a);
        Assert.Contains("ComsStation", d);
        Assert.Contains("AntiAirBattery1", d);
        Assert.Contains("AntiAirBattery2", d);
        Assert.DoesNotContain("LandingPad1", d);
    }

    [Fact]
    public void ZonesFollowTheLayout()
    {
        var zones = Season("guildWarSeasonA").Zones;

        Assert.Equal(15, zones.Count);
        Assert.Equal(new GameCatalogGuildWarZone("Trenches1", "trenches", null, false, 10000, 0, 0), zones[0]);
        Assert.Equal(new GameCatalogGuildWarZone("HQ", "hq", null, true, 40000, 4, 1), zones.Single(zone => zone.Id == "HQ"));
        Assert.Equal("EnvArmourSupplies_Regional", zones.Single(zone => zone.Id == "Armoury").BuffId);
    }

    [Fact]
    public void TiersAreLowestFirst()
    {
        var tiers = Season("guildWarSeasonA").Tiers;

        Assert.Equal(["Trooper", "Veteran", "Elite", "Hero"], tiers.Select(tier => tier.Id));
        Assert.Equal(["Mythic", "Mythic", "Legendary", "Legendary", "Legendary"], tiers[^1].RarityCaps);
    }

    [Fact]
    public void ArmouryAtLevelFiveIsElite()
    {
        var season = Season("guildWarSeasonA");
        var level = season.Levels.Single(level => level.BattlefieldLevel == 5);
        var tierId = level.Zones.Single(zone => zone.ZoneId == "Armoury").TierId;

        Assert.Equal("Elite", tierId);
        Assert.Equal(["Legendary", "Legendary", "Epic", "Epic", "Epic"], season.Tiers.Single(tier => tier.Id == tierId).RarityCaps);
        Assert.Equal(10, level.AttemptDebuffPercent);
        Assert.Equal(400, level.ScoreFactorPercent);
    }

    [Fact]
    public void LevelZeroHasNoScoreFactor()
    {
        var levels = Season("guildWarSeasonB").Levels;

        Assert.Equal([0, 1, 2, 3, 4, 5], levels.Select(level => level.BattlefieldLevel));
        Assert.Null(levels[0].ScoreFactorPercent);
        Assert.Equal(0, levels[0].AttemptDebuffPercent);
    }

    [Fact]
    public void RarityKeyedBuffAbilitiesAreResolved()
    {
        var buff = Season("guildWarSeasonA").Buffs.Single(buff => buff.Id == "EnvAngelsOfDeath_Regional");

        Assert.Equal("Regional", buff.Scope);
        Assert.Equal(
            ["Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythic"],
            buff.Abilities.Select(ability => ability.Rarity));
        Assert.Equal("EnvAngelsOfDeath_Common", buff.Abilities[0].AbilityId);
    }

    [Fact]
    public void PlainBuffAbilitiesKeepTheirVariables()
    {
        var buff = Season("guildWarSeasonA").Buffs.Single(buff => buff.Id == "EnvArmourSupplies_Regional");
        var ability = Assert.Single(buff.Abilities);

        Assert.Null(ability.Rarity);
        Assert.Equal(10, ability.Variables["extraArmorPct"][0]);
    }

    [Fact]
    public void MapsScoringAndPowerCapsAreServed()
    {
        var season = Season("guildWarSeasonA");

        Assert.Equal(10, season.MaxAttempts);
        Assert.Equal("guildWarSeasonB", season.NextSeasonId);
        Assert.Equal(15, season.Maps.Count);
        Assert.Equal(new GameCatalogGuildWarMap("EC1_09", 2), season.Maps[0]);
        Assert.Equal(200, season.Scoring.UnitKilled);
        Assert.Equal(new GameCatalogGuildWarLineupScore(1, 0, 600), season.Scoring.LineupEliminated[0]);
        Assert.Equal(["Common", "Uncommon", "Rare", "Epic", "Legendary"], season.PowerCaps.Select(cap => cap.Rarity));
    }

    [Fact]
    public void ALevelMissingAZoneFailsTheLoad()
    {
        var season = Valid() with
        {
            Levels = [new DatamineGuildWarLevel(1, 100, 30, [new DatamineGuildWarLevelZone("Zone1", "Trooper")])],
        };

        var error = Assert.Throws<InvalidOperationException>(() => Read(season));
        Assert.Contains("every layout zone", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownBuffFailsTheLoad()
    {
        var season = Valid() with
        {
            ZoneTypeConfigs = new() { ["Zone1"] = new("z", true, "Missing_Regional"), ["Zone2"] = new("z", true, null) },
        };

        Assert.Throws<InvalidOperationException>(() => Read(season));
    }

    [Fact]
    public void ABuffWithNoAbilityFailsTheLoad()
    {
        var season = Valid() with { Buffs = new() { ["Buff_Global"] = new("Nothing", "Global") } };

        Assert.Throws<InvalidOperationException>(() => Read(season));
    }

    [Fact]
    public void TestSeasonsAreDropped()
    {
        var seasons = Read(Valid(), ("guildWarSeasonA_staging", Valid()), ("guildWarSeasonA_ci2", Valid()));

        Assert.Equal(["guildWarSeasonA"], seasons.Select(season => season.Id));
    }

    private static IReadOnlyList<GameCatalogGuildWarSeason> Read(
        DatamineGuildWarSeason season, params (string Id, DatamineGuildWarSeason Season)[] extra)
    {
        var configs = new Dictionary<string, DatamineGuildWarSeason> { ["guildWarSeasonA"] = season };
        foreach (var (id, other) in extra)
        {
            configs[id] = other;
        }

        return GameCatalogDenormalizer.ReadDatamineGuildWar(
            new DatamineGuildWar(configs, new DatamineGuildWarConfig(new() { ["Common"] = 330 })));
    }

    private static DatamineGuildWarSeason Valid() => new(
        MaxAttempts: 10,
        ScorePerZoneType: new() { ["Zone1"] = 100, ["Zone2"] = 200 },
        ScoreFromUnitKilled: 200,
        DefaultZoneTypeLayout: [["Zone1", "Zone2"]],
        ZoneTypeConfigs: new() { ["Zone1"] = new("z", true, "Buff_Global"), ["Zone2"] = new("z", true, null) },
        MapPool: [new DatamineGuildWarMap("C1_01", 1)],
        Abilities: new() { ["Buff"] = new(new() { ["hpPct"] = [10, 20] }) },
        Levels:
        [
            new DatamineGuildWarLevel(1, 100, 30,
                [new DatamineGuildWarLevelZone("Zone1", "Trooper"), new DatamineGuildWarLevelZone("Zone2", "Trooper")]),
        ],
        ZoneTiers: [new DatamineGuildWarZoneTier("Trooper", ["Rare", "Rare", "Uncommon", "Uncommon", "Uncommon"])],
        ScoreForLineupEliminated: [new DatamineGuildWarLineupScore(1, 0, 600)],
        Buffs: new() { ["Buff_Global"] = new("Buff", "Global") },
        NextGuildWarSeasonConfigId: "guildWarSeasonA");
}
