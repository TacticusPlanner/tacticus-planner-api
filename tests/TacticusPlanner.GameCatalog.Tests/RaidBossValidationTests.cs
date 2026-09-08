using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class RaidBossValidationTests
{
    private static GameCatalogRaidBossRawStat Stat() =>
        new(1000, 10, 5, 0, 1, "Common", 0, 1, null, null, null, null, null);

    private static GameCatalogRaidBossRawUnitSet UnitSet(
        string? faction = "Tyranids",
        IReadOnlyList<GameCatalogRaidBossRawStat>? stats = null,
        IReadOnlyList<string>? traits = null) =>
        new(faction!, 4, null, null, stats ?? [Stat()], null, null, null, null, traits);

    private static GameCatalogRaidBossRawSeason Season(params GameCatalogRaidBossRawEncounter[] encounters) =>
        new("season-1", null, null,
        [
            new GameCatalogRaidBossRawTier(6, [new GameCatalogRaidBossRawSet(0, "chest-0", 100, encounters)]),
        ]);

    private static GameCatalogRaidBossRawEncounter Encounter(
        string unitId = "GuildBoss1Boss1Tyran:1",
        string type = "Boss",
        IReadOnlyList<string>? enemies = null,
        IReadOnlyList<GameCatalogRaidBossRawEncounterModifier>? modifiers = null) =>
        new(0, type, "GB_01", 6, null, unitId, null, null, enemies, null, modifiers);

    private static List<GameCatalogValidationError> Validate(
        IReadOnlyDictionary<string, GameCatalogRaidBossRawUnitSet> unitSets,
        IReadOnlyDictionary<string, GameCatalogRaidBossRawSeason>? seasons = null,
        IReadOnlyDictionary<string, GameCatalogRaidBossRawModifier>? modifiers = null,
        IReadOnlyList<string>? primes = null)
    {
        var allUnitSets = new Dictionary<string, GameCatalogRaidBossRawUnitSet>(unitSets);
        // Every validation case needs at least one prime so the "no primes" guard does not mask the
        // assertion under test.
        foreach (var primeId in primes ?? ["GuildBoss1MiniBoss1TyranWarrior"])
        {
            allUnitSets.TryAdd(primeId, UnitSet());
        }

        var raw = new GameCatalogRaidBossRawData(
            ["season-1"], [], allUnitSets,
            seasons ?? new Dictionary<string, GameCatalogRaidBossRawSeason>(),
            modifiers ?? new Dictionary<string, GameCatalogRaidBossRawModifier>());

        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateRaidBosses(raw, GameCatalogDenormalizer.BuildRaidBosses(raw), errors);
        return errors;
    }

    [Fact]
    public void CleanDataProducesNoErrors()
    {
        var errors = Validate(
            new Dictionary<string, GameCatalogRaidBossRawUnitSet>
            {
                ["GuildBoss1Boss1Tyran"] = UnitSet(traits: ["trait-a"]),
                ["GuildBoss1Npc1Termagant"] = UnitSet(faction: null, stats: []),
            },
            seasons: new Dictionary<string, GameCatalogRaidBossRawSeason>
            {
                ["season-1"] = Season(Encounter(
                    enemies: ["GuildBoss1Npc1Termagant:1"],
                    modifiers: [new GameCatalogRaidBossRawEncounterModifier(25, "mod-a")])),
            },
            modifiers: new Dictionary<string, GameCatalogRaidBossRawModifier>
            {
                ["mod-a"] = new("bossStatDecrease", "movement", null, 1),
            });

        Assert.Empty(errors);
    }

    [Fact]
    public void UnresolvedEncounterUnitFailsValidation()
    {
        var errors = Validate(
            new Dictionary<string, GameCatalogRaidBossRawUnitSet> { ["GuildBoss1Boss1Tyran"] = UnitSet() },
            seasons: new Dictionary<string, GameCatalogRaidBossRawSeason>
            {
                ["season-1"] = Season(Encounter(unitId: "GuildBoss9Boss1Missing:1")),
            });

        Assert.Contains(errors, error => error.Code == "MissingReference" && error.Message.Contains("unitId"));
    }

    [Fact]
    public void UnresolvedFieldNpcFailsValidation()
    {
        var errors = Validate(
            new Dictionary<string, GameCatalogRaidBossRawUnitSet> { ["GuildBoss1Boss1Tyran"] = UnitSet() },
            seasons: new Dictionary<string, GameCatalogRaidBossRawSeason>
            {
                ["season-1"] = Season(Encounter(enemies: ["GuildBoss1Npc9Missing:1"])),
            });

        Assert.Contains(errors, error => error.Code == "MissingReference" && error.Message.Contains("fieldNpcIds"));
    }

    [Fact]
    public void UnresolvedModifierIdFailsValidation()
    {
        var errors = Validate(
            new Dictionary<string, GameCatalogRaidBossRawUnitSet> { ["GuildBoss1Boss1Tyran"] = UnitSet() },
            seasons: new Dictionary<string, GameCatalogRaidBossRawSeason>
            {
                ["season-1"] = Season(Encounter(
                    modifiers: [new GameCatalogRaidBossRawEncounterModifier(25, "mod-missing")])),
            });

        Assert.Contains(errors, error => error.Code == "MissingReference" && error.Message.Contains("modifier"));
    }

    [Fact]
    public void EmptyProgressionOnABossFailsValidation()
    {
        var errors = Validate(new Dictionary<string, GameCatalogRaidBossRawUnitSet>
        {
            ["GuildBoss1Boss1Tyran"] = UnitSet(stats: []),
        });

        Assert.Contains(errors, error => error.Code == "EmptyProgression");
    }

    [Fact]
    public void UnrecognizedEncounterTypeFailsValidation()
    {
        var errors = Validate(
            new Dictionary<string, GameCatalogRaidBossRawUnitSet> { ["GuildBoss1Boss1Tyran"] = UnitSet() },
            seasons: new Dictionary<string, GameCatalogRaidBossRawSeason>
            {
                ["season-1"] = Season(Encounter(type: "Ambush")),
            });

        Assert.Contains(errors, error => error.Code == "InvalidEncounterType");
    }

    [Fact]
    public void MissingFactionOnABossFailsValidation()
    {
        var errors = Validate(new Dictionary<string, GameCatalogRaidBossRawUnitSet>
        {
            ["GuildBoss1Boss1Tyran"] = UnitSet(faction: null),
        });

        Assert.Contains(errors, error => error.Code == "RequiredField" && error.Message.Contains("factionId"));
    }

    [Fact]
    public void NoBossesOrNoPrimesFailsValidation()
    {
        var raw = new GameCatalogRaidBossRawData(
            ["season-1"], [],
            new Dictionary<string, GameCatalogRaidBossRawUnitSet> { ["GuildBoss1Boss1Tyran"] = UnitSet() },
            new Dictionary<string, GameCatalogRaidBossRawSeason>(),
            new Dictionary<string, GameCatalogRaidBossRawModifier>());

        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateRaidBosses(raw, GameCatalogDenormalizer.BuildRaidBosses(raw), errors);

        Assert.Contains(errors, error => error.Code == "EmptyDataset" && error.Message.Contains("primes"));
    }
}
