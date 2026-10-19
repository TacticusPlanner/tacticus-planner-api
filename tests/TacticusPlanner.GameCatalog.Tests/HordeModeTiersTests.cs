using System.Text.Json;
using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Utils;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class HordeModeTiersTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> CharacterIds =
        new(["bloodDante", "ultraCalgar", "ultraTigurius"], StringComparer.Ordinal);

    private static readonly GameCatalogHordeModeTier[] DefaultTiers =
    [
        new("S", 1.3),
        new("A", 1.2),
        new("B", 1.1),
        new("C", 0.9),
        new("D", 0.8),
    ];

    private static GameCatalogHordeModeTiers Record(params GameCatalogHordeModeCharacter[] characters) =>
        new(GameCatalogDatasets.HordeModeTiers, DefaultTiers, characters);

    // Relic id -> the units allowed to equip it.
    private static readonly Dictionary<string, IReadOnlySet<string>> RelicAllowedUnits = new(StringComparer.Ordinal)
    {
        ["R_Crit_Lakrimae"] = new HashSet<string>(["ultraTigurius"], StringComparer.Ordinal),
        ["R_Block_DaemonfleshPlate"] = new HashSet<string>(["ultraTigurius", "ultraCalgar"], StringComparer.Ordinal),
    };

    private static List<GameCatalogValidationError> Validate(params GameCatalogHordeModeTiers[] records)
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateHordeModeTiers(records, CharacterIds, errors, RelicAllowedUnits);
        return errors;
    }

    private static GameCatalogHordeModeCharacter WithRelics(string unitId, string tier, double? multiplier, params GameCatalogHordeModeRelic[] relics) =>
        new(unitId, tier, multiplier, Relics: relics);

    private static void AssertSingleError(List<GameCatalogValidationError> errors, params string[] expectedFragments)
    {
        var error = Assert.Single(errors);
        Assert.Equal(GameCatalogDatasets.HordeModeTiers, error.Dataset);
        Assert.Equal("InvalidHordeModeTiers", error.Code);
        Assert.Contains("horde-mode-tiers", error.Message, StringComparison.Ordinal);
        foreach (var fragment in expectedFragments)
        {
            Assert.Contains(fragment, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void InitialDatasetLoadsTheFiveTiersAndTheCuratedCharacters()
    {
        var snapshot = GameCatalogLoader.Load();

        var record = Assert.Single(snapshot.HordeModeTiersViews);
        Assert.Equal("horde-mode-tiers", record.Id);
        Assert.Equal(["S", "A", "B", "C", "D"], record.Tiers.Select(tier => tier.Tier));
        Assert.Equal([1.9, 1.7, 1.45, 1.2, 1.0], record.Tiers.Select(tier => tier.Multiplier));
        Assert.Equal(47, record.Characters.Count);
        Assert.Contains(record.Characters, character => character is { UnitId: "custoBladeChampion", Tier: "S", Multiplier: 2.0 });
        Assert.Contains(record.Characters, character => character is { UnitId: "deathBlightlord", Tier: "S", Multiplier: 1.9 });
        Assert.Contains(record.Characters, character => character is { UnitId: "blackTerminator", Tier: "A", Multiplier: 1.76 });
        Assert.Contains(record.Characters, character => character is { UnitId: "ultraEliminatorSgt", Tier: "D", Multiplier: 1.0 });
    }

    [Fact]
    public void InitialDatasetServesTheSeedSynergiesInAuthoringOrder()
    {
        var characters = Assert.Single(GameCatalogLoader.Load().HordeModeTiersViews).Characters;

        Assert.Equal(
            ["deathRotbone", "worldTerminator", "blackAbaddon"],
            Assert.Single(characters, character => character.UnitId == "blackTerminator").Synergies);
        Assert.Null(Assert.Single(characters, character => character.UnitId == "custoBladeChampion").Synergies);
        Assert.Equal(9, characters.Count(character => character.Synergies is not null));
    }

    [Fact]
    public void DatasetIsListedInTheManifestWithAHash()
    {
        var snapshot = GameCatalogLoader.Load();

        var entry = Assert.Single(snapshot.Manifest.Datasets, dataset => dataset.Key == GameCatalogDatasets.HordeModeTiers);
        Assert.False(string.IsNullOrWhiteSpace(entry.Hash));
        Assert.Contains(GameCatalogDatasets.HordeModeTiers, GameCatalogDatasets.Served);
    }

    [Fact]
    public void CharactersAreSortedByUnitIdAndTheHashIgnoresAuthoringOrder()
    {
        var served = GameCatalogDenormalizer.BuildHordeModeTiers([Record(new("ultraTigurius", "S"), new("bloodDante", "C"))]);
        var servedReversed = GameCatalogDenormalizer.BuildHordeModeTiers([Record(new("bloodDante", "C"), new("ultraTigurius", "S"))]);

        Assert.Equal(["bloodDante", "ultraTigurius"], Assert.Single(served).Characters.Select(character => character.UnitId));
        Assert.Equal(
            GameCatalogHashing.ComputeCanonicalJsonHash(served, JsonOptions),
            GameCatalogHashing.ComputeCanonicalJsonHash(servedReversed, JsonOptions));
    }

    [Fact]
    public void MissingListsAreServedEmpty()
    {
        var served = Assert.Single(GameCatalogDenormalizer.BuildHordeModeTiers([Record() with { Tiers = null!, Characters = null! }]));

        Assert.Empty(served.Tiers);
        Assert.Empty(served.Characters);
    }

    [Fact]
    public void ValidRecordPasses() =>
        Assert.Empty(Validate(Record(new("ultraTigurius", "S"), new("ultraCalgar", "C"))));

    [Fact]
    public void WrongRecordIdFails() =>
        AssertSingleError(Validate(Record() with { Id = "not-the-id" }), "not-the-id");

    [Fact]
    public void UnknownUnitFails() =>
        AssertSingleError(Validate(Record(new GameCatalogHordeModeCharacter("notAUnit", "A"))), "notAUnit");

    [Fact]
    public void MachineOfWarIdFails()
    {
        // Validate against the real catalog roster: a MoW id is a unit, but not a character.
        var snapshot = GameCatalogLoader.Load();
        Assert.Contains(snapshot.Mows, mow => mow.Id == "necroReanimator");
        var characterIds = new HashSet<string>(snapshot.Characters.Select(character => character.Id), StringComparer.Ordinal);

        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateHordeModeTiers([Record(new GameCatalogHordeModeCharacter("necroReanimator", "A"))], characterIds, errors);

        AssertSingleError(errors, "necroReanimator");
    }

    [Fact]
    public void DuplicateUnitFails() =>
        AssertSingleError(Validate(Record(new GameCatalogHordeModeCharacter("ultraCalgar", "A"), new GameCatalogHordeModeCharacter("ultraCalgar", "B"))), "ultraCalgar");

    [Fact]
    public void UnknownTierFails() =>
        AssertSingleError(Validate(Record(new GameCatalogHordeModeCharacter("ultraCalgar", "E"))), "ultraCalgar", "'E'");

    [Fact]
    public void TierDPasses() =>
        Assert.Empty(Validate(Record(new GameCatalogHordeModeCharacter("ultraTigurius", "D", 1.0))));

    [Fact]
    public void TiersOutOfOrderFail() =>
        AssertSingleError(
            Validate(Record() with { Tiers = [new("A", 1.2), new("S", 1.3), new("B", 1.1), new("C", 0.9), new("D", 0.8)] }),
            "'A, S, B, C, D'");

    [Fact]
    public void MissingTierFails() =>
        AssertSingleError(Validate(Record() with { Tiers = DefaultTiers[..3] }), "'S, A, B'");

    [Fact]
    public void MissingTierDFails() =>
        AssertSingleError(Validate(Record() with { Tiers = DefaultTiers[..4] }), "'S, A, B, C'", "'S, A, B, C, D'");

    [Theory]
    [InlineData(0)]
    [InlineData(5.1)]
    [InlineData(-1)]
    public void MultiplierOutOfBoundsFails(double multiplier) =>
        AssertSingleError(
            Validate(Record() with { Tiers = [new("S", multiplier), .. DefaultTiers[1..]] }),
            "tier 'S'",
            $"'{multiplier.ToString(System.Globalization.CultureInfo.InvariantCulture)}'");

    [Fact]
    public void MultiplierUpperBoundIsInclusive() =>
        Assert.Empty(Validate(Record() with { Tiers = [new("S", 5), .. DefaultTiers[1..]] }));

    [Fact]
    public void CharacterMultiplierOverridePasses() =>
        Assert.Empty(Validate(Record(new("ultraTigurius", "S", 1.45), new("ultraCalgar", "C", 5))));

    [Theory]
    [InlineData(0)]
    [InlineData(5.1)]
    [InlineData(-1)]
    public void CharacterMultiplierOverrideOutOfBoundsFails(double multiplier) =>
        AssertSingleError(
            Validate(Record(new GameCatalogHordeModeCharacter("ultraTigurius", "S", multiplier))),
            "ultraTigurius",
            $"multiplier '{multiplier.ToString(System.Globalization.CultureInfo.InvariantCulture)}'");

    [Fact]
    public void CharacterMultiplierIsSerializedOnlyWhenSet()
    {
        var json = JsonSerializer.SerializeToNode(
            Record(new("ultraTigurius", "S", 1.45), new("ultraCalgar", "C")), JsonOptions)!;
        var characters = json["characters"]!.AsArray();

        Assert.Equal(["unitId", "tier", "multiplier"], characters[0]!.AsObject().Select(property => property.Key));
        Assert.Equal(1.45, characters[0]!["multiplier"]!.GetValue<double>());
        Assert.Equal(["unitId", "tier"], characters[1]!.AsObject().Select(property => property.Key));
    }

    [Fact]
    public void CharacterMultiplierIsReadFromTheRawFile()
    {
        var record = JsonSerializer.Deserialize<GameCatalogHordeModeTiers>(
            """{"id":"horde-mode-tiers","tiers":[],"characters":[{"unitId":"a","tier":"S","multiplier":1.4},{"unitId":"b","tier":"A"}]}""",
            JsonOptions)!;

        Assert.Equal(1.4, record.Characters[0].Multiplier);
        Assert.Null(record.Characters[1].Multiplier);
    }

    [Fact]
    public void InitialDatasetServesTheSeedRelicRatings()
    {
        var characters = Assert.Single(GameCatalogLoader.Load().HordeModeTiersViews).Characters;

        var revas = Assert.Single(characters, character => character.UnitId == "tauCrisis");
        var relic = Assert.Single(revas.Relics!);
        Assert.Equal(new GameCatalogHordeModeRelic("R_Crit_Dw02AdvancedBurstCannon", 1.98), relic);
        Assert.Null(Assert.Single(characters, character => character.UnitId == "ultraCalgar").Relics);
        Assert.Equal(8, characters.Count(character => character.Relics is not null));
    }

    [Fact]
    public void RelicsAreSortedByEquipmentIdAndTheHashIgnoresAuthoringOrder()
    {
        var lakrimae = new GameCatalogHordeModeRelic("R_Crit_Lakrimae", 1.5);
        var plate = new GameCatalogHordeModeRelic("R_Block_DaemonfleshPlate", 1.6);
        var served = GameCatalogDenormalizer.BuildHordeModeTiers([Record(WithRelics("ultraTigurius", "S", null, lakrimae, plate))]);
        var servedReversed = GameCatalogDenormalizer.BuildHordeModeTiers([Record(WithRelics("ultraTigurius", "S", null, plate, lakrimae))]);

        Assert.Equal(
            ["R_Block_DaemonfleshPlate", "R_Crit_Lakrimae"],
            Assert.Single(Assert.Single(served).Characters).Relics!.Select(relic => relic.EquipmentId));
        Assert.Equal(
            GameCatalogHashing.ComputeCanonicalJsonHash(served, JsonOptions),
            GameCatalogHashing.ComputeCanonicalJsonHash(servedReversed, JsonOptions));
    }

    [Fact]
    public void EmptyRelicsAreServedAsNotSet() =>
        Assert.Null(Assert.Single(Assert.Single(
            GameCatalogDenormalizer.BuildHordeModeTiers([Record(WithRelics("ultraTigurius", "S", null))])).Characters).Relics);

    [Fact]
    public void ValidRelicsPass() =>
        Assert.Empty(Validate(Record(WithRelics("ultraTigurius", "S", 1.4, new GameCatalogHordeModeRelic("R_Crit_Lakrimae", 1.5), new GameCatalogHordeModeRelic("R_Block_DaemonfleshPlate", 1.4)))));

    [Fact]
    public void RelicTheCharacterCannotEquipFails() =>
        AssertSingleError(Validate(Record(WithRelics("ultraCalgar", "S", null, new GameCatalogHordeModeRelic("R_Crit_Lakrimae", 1.5)))), "ultraCalgar", "R_Crit_Lakrimae");

    [Fact]
    public void NonRelicFails() =>
        AssertSingleError(Validate(Record(WithRelics("ultraTigurius", "S", null, new GameCatalogHordeModeRelic("I_Crit_C001", 1.5)))), "ultraTigurius", "I_Crit_C001");

    [Fact]
    public void DuplicateRelicFails() =>
        AssertSingleError(
            Validate(Record(WithRelics("ultraTigurius", "S", null, new GameCatalogHordeModeRelic("R_Crit_Lakrimae", 1.5), new GameCatalogHordeModeRelic("R_Crit_Lakrimae", 1.6)))),
            "ultraTigurius",
            "'R_Crit_Lakrimae' is listed more than once");

    [Fact]
    public void RelicBelowTheBaseMultiplierFails() =>
        AssertSingleError(
            Validate(Record(WithRelics("ultraTigurius", "S", 1.92, new GameCatalogHordeModeRelic("R_Crit_Lakrimae", 1.5)))),
            "ultraTigurius",
            "'1.5'",
            "below");

    [Fact]
    public void RelicBelowTheTierMultiplierFails() =>
        AssertSingleError(Validate(Record(WithRelics("ultraTigurius", "S", null, new GameCatalogHordeModeRelic("R_Crit_Lakrimae", 1.2)))), "ultraTigurius", "'1.3'");

    [Theory]
    [InlineData(0)]
    [InlineData(5.1)]
    public void RelicMultiplierOutOfBoundsFails(double multiplier) =>
        AssertSingleError(
            Validate(Record(WithRelics("ultraTigurius", "S", null, new GameCatalogHordeModeRelic("R_Crit_Lakrimae", multiplier)))),
            "ultraTigurius",
            "must be in (0, 5]");

    [Fact]
    public void RelicsAreSerializedOnlyWhenSet()
    {
        var json = JsonSerializer.SerializeToNode(
            Record(WithRelics("ultraTigurius", "S", 1.4, new GameCatalogHordeModeRelic("R_Crit_Lakrimae", 1.5)), new("ultraCalgar", "C")), JsonOptions)!;
        var characters = json["characters"]!.AsArray();

        Assert.Equal(["unitId", "tier", "multiplier", "relics"], characters[0]!.AsObject().Select(property => property.Key));
        Assert.Equal(["equipmentId", "multiplier"], characters[0]!["relics"]![0]!.AsObject().Select(property => property.Key));
        Assert.Equal(["unitId", "tier"], characters[1]!.AsObject().Select(property => property.Key));
    }

    [Fact]
    public void SynergiesKeepTheirOrderAndAnEmptyListIsServedAsNotSet()
    {
        var served = Assert.Single(GameCatalogDenormalizer.BuildHordeModeTiers([Record(
            new GameCatalogHordeModeCharacter("ultraTigurius", "S", Synergies: ["ultraCalgar", "bloodDante"]),
            new GameCatalogHordeModeCharacter("ultraCalgar", "A", Synergies: []))]));

        Assert.Equal(["ultraCalgar", "bloodDante"], served.Characters.Single(character => character.UnitId == "ultraTigurius").Synergies);
        Assert.Null(served.Characters.Single(character => character.UnitId == "ultraCalgar").Synergies);
    }

    [Fact]
    public void SynergiesAreSerializedOnlyWhenSet()
    {
        var json = JsonSerializer.SerializeToNode(
            Record(new GameCatalogHordeModeCharacter("ultraTigurius", "S", Synergies: ["ultraCalgar"]), new("ultraCalgar", "C")), JsonOptions)!;
        var characters = json["characters"]!.AsArray();

        Assert.Equal(["unitId", "tier", "synergies"], characters[0]!.AsObject().Select(property => property.Key));
        Assert.Equal(["unitId", "tier"], characters[1]!.AsObject().Select(property => property.Key));
    }

    [Fact]
    public void ValidSynergiesPass() =>
        Assert.Empty(Validate(Record(new GameCatalogHordeModeCharacter("ultraTigurius", "S", Synergies: ["ultraCalgar", "bloodDante"]))));

    [Fact]
    public void UnknownSynergyFails() =>
        AssertSingleError(Validate(Record(new GameCatalogHordeModeCharacter("ultraTigurius", "S", Synergies: ["notAUnit"]))), "ultraTigurius", "notAUnit");

    [Fact]
    public void SelfSynergyFails() =>
        AssertSingleError(Validate(Record(new GameCatalogHordeModeCharacter("ultraTigurius", "S", Synergies: ["ultraTigurius"]))), "ultraTigurius", "itself");

    [Fact]
    public void DuplicateSynergyFails() =>
        AssertSingleError(
            Validate(Record(new GameCatalogHordeModeCharacter("ultraTigurius", "S", Synergies: ["ultraCalgar", "ultraCalgar"]))),
            "ultraTigurius",
            "'ultraCalgar' is listed more than once");

    [Fact]
    public void MoreThanSixSynergiesFail()
    {
        // Seven distinct real character ids, so only the count fails.
        var snapshot = GameCatalogLoader.Load();
        var characterIds = new HashSet<string>(snapshot.Characters.Select(character => character.Id), StringComparer.Ordinal);
        var synergies = characterIds.Where(id => id != "ultraTigurius").Order(StringComparer.Ordinal).Take(7).ToArray();

        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateHordeModeTiers([Record(new GameCatalogHordeModeCharacter("ultraTigurius", "S", Synergies: synergies))], characterIds, errors);

        AssertSingleError(errors, "ultraTigurius", "7 synergies", "at most 6");
    }

    [Fact]
    public void NotExactlyOneRecordFails()
    {
        AssertSingleError(Validate(), "exactly one record", "0");
        AssertSingleError(Validate(Record(), Record()), "exactly one record", "2");
    }
}
