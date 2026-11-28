using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class GuildWarDenormalizerTests
{
    private static readonly IReadOnlyList<GameCatalogGuildWarSeason> Shipped = GameCatalogCompiler.Compile().GuildWarViews;

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
        Assert.Equal(("EC1_09", 2), (season.Maps[0].BoardId, season.Maps[0].SpawnPointsSet));
        Assert.Equal(200, season.Scoring.UnitKilled);
        Assert.Equal(new GameCatalogGuildWarLineupScore(1, 0, 600), season.Scoring.LineupEliminated[0]);
        Assert.Equal(["Common", "Uncommon", "Rare", "Epic", "Legendary"], season.PowerCaps.Select(cap => cap.Rarity));
    }

    [Fact]
    public void MapsCarryTheirBoardGrid()
    {
        var map = Season("guildWarSeasonA").Maps.Single(map => map.BoardId == "PVP_desert_10");
        var board = map.Board;

        Assert.Equal(1, map.SpawnPointsSet);
        Assert.Equal((7, 10), (board.Columns, board.Rows));
        Assert.Equal((13, 15, 3, 2), (board.VisualColumns, board.VisualRows, board.VisualFirstColumn, board.VisualFirstRow));
        Assert.Equal(70, board.Tiles.Count);
        Assert.Equal(new GameCatalogGuildWarTile(0, 0, "Block", 2, true), board.Tiles[0]);
        Assert.Equal(new GameCatalogGuildWarTile(0, 1, "Grass", 2, false), board.Tiles[7]); // row 0 first, then row 1
        Assert.Equal(
            [new(0, 2), new(1, 3), new(3, 1), new(5, 0), new(6, 0)],
            board.AttackerSpawns.ToArray<GameCatalogGuildWarCell>());
        Assert.Equal(20, board.DefenderSpawns.Count);
    }

    [Fact]
    public void SpawnPointsSetIsZeroBased()
    {
        var board = Season("guildWarSeasonA").Maps.Single(map => map.BoardId == "LHE_Desert_06").Board;

        Assert.Equal(5, board.AttackerSpawns.Count);
        Assert.True(board.Tiles.Single(tile => (tile.Column, tile.Row) == (1, 0)).Blocked); // forced unplayable Grass
    }

    [Theory]
    [InlineData("spawnPointsSet 3 is not one of its 1 spawn point sets", 3, "C1_01", 0)]
    [InlineData("the file is board 'C1_02'", 0, "C1_02", 0)]
    [InlineData("lacks attacker or defender spawns", 0, "C1_01", 1)]
    public void ABoardThatDoesNotFitFailsTheLoad(string message, int spawnPointsSet, string fileId, int attackerTeam)
    {
        var season = Valid() with { MapPool = [new DatamineGuildWarMap("C1_01", spawnPointsSet)] };

        var error = Assert.Throws<InvalidOperationException>(() => Read(season, Board(fileId, attackerTeam)));

        Assert.Contains(message, error.Message);
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
        DatamineGuildWarSeason season, params (string Id, DatamineGuildWarSeason Season)[] extra) =>
        Read(season, Board("C1_01", 0), extra);

    private static IReadOnlyList<GameCatalogGuildWarSeason> Read(
        DatamineGuildWarSeason season, DatamineBoard board, params (string Id, DatamineGuildWarSeason Season)[] extra)
    {
        var configs = new Dictionary<string, DatamineGuildWarSeason> { ["guildWarSeasonA"] = season };
        foreach (var (id, other) in extra)
        {
            configs[id] = other;
        }

        return GameCatalogDenormalizer.ReadDatamineGuildWar(
            new DatamineGuildWar(configs, new DatamineGuildWarConfig(new() { ["Common"] = 330 })),
            _ => board);
    }

    // A one-hex board whose single spawn point set puts team attackerTeam on (0, 0) and the other team on (0, 0) too.
    private static DatamineBoard Board(string id, int attackerTeam) => new(
        new DatamineBoardConfiguration(
            id,
            1,
            1,
            [new DatamineBoardColumn([new DatamineBoardTile("Grass", 2, 0)])],
            [
                new DatamineBoardSpawnPointSet(
                [
                    new DatamineBoardSpawnPointGroup(attackerTeam, [new DatamineBoardSpawnPoint(0, 0)]),
                    new DatamineBoardSpawnPointGroup(1, [new DatamineBoardSpawnPoint(0, 0)]),
                ]),
            ]),
        new DatamineBoardVisual(3, 3, 1, 1));

    private static DatamineGuildWarSeason Valid() => new(
        MaxAttempts: 10,
        ScorePerZoneType: new() { ["Zone1"] = 100, ["Zone2"] = 200 },
        ScoreFromUnitKilled: 200,
        DefaultZoneTypeLayout: [["Zone1", "Zone2"]],
        ZoneTypeConfigs: new() { ["Zone1"] = new("z", true, "Buff_Global"), ["Zone2"] = new("z", true, null) },
        MapPool: [new DatamineGuildWarMap("C1_01", 0)],
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
