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

    private static List<GameCatalogValidationError> Validate(params GameCatalogHordeModeTiers[] records)
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateHordeModeTiers(records, CharacterIds, errors);
        return errors;
    }

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
    public void NotExactlyOneRecordFails()
    {
        AssertSingleError(Validate(), "exactly one record", "0");
        AssertSingleError(Validate(Record(), Record()), "exactly one record", "2");
    }
}
