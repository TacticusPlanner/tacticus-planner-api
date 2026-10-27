using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class FactionDenormalizerTests
{
    private static readonly Lazy<GameCatalogSnapshot> Catalog = new(GameCatalogLoader.Load);

    private static GameCatalogSnapshot Snapshot => Catalog.Value;

    [Fact]
    public void TheShippedFactionsFollowUnlockOrder()
    {
        var factions = Snapshot.FactionViews;

        Assert.Equal(23, factions.Count);
        Assert.Equal(new GameCatalogFaction("Ultramarines", "Imperial", "2022-08-14T00:00:00Z", false), factions[0]);
        Assert.Equal(
            new GameCatalogFaction("TheLostAndTheDamned", "Chaos", "2026-11-03T16:00:00Z", true),
            factions[^1]);
        Assert.Equal(["AdeptusAstartes", "TheLostAndTheDamned"], factions.Where(faction => faction.CollectionFaction).Select(faction => faction.Id));
    }

    [Fact]
    public void FactionsWithTheSameUnlockTimeAreOrderedById()
    {
        var factions = GameCatalogDenormalizer.ReadDatamineFactions(new Dictionary<string, DatamineFaction>
        {
            ["Tau"] = new(["Xenos"], 2, null),
            ["Orks"] = new(["Xenos"], 1, null),
            ["Necrons"] = new(["Xenos"], 2, null),
        });

        Assert.Equal(["Orks", "Necrons", "Tau"], factions.Select(faction => faction.Id));
    }

    [Theory]
    [InlineData(new string[0], 1L)]
    [InlineData(new[] { "Imperial", "Chaos" }, 1L)]
    [InlineData(new[] { "Neutral" }, 1L)]
    [InlineData(new[] { "Imperial" }, 0L)]
    public void AFactionWithoutOneAllianceOrUnlockTimeFailsTheLoad(string[] traits, long unlockTimestamp)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            GameCatalogDenormalizer.ReadDatamineFactions(new Dictionary<string, DatamineFaction>
            {
                ["Orks"] = new(traits, unlockTimestamp, null),
            }));

        Assert.Contains("'factions' faction 'Orks'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShippedDamageTypesAreOrderedByIdWithTheirTraits()
    {
        var damageTypes = Snapshot.DamageTypeViews;

        Assert.Equal(22, damageTypes.Count);
        Assert.Equal(damageTypes.Select(damageType => damageType.Id).Order(StringComparer.Ordinal), damageTypes.Select(damageType => damageType.Id));
        Assert.Equal(new GameCatalogDamageType("Acid", 40, []), damageTypes[0]);
        Assert.Equal(["OnFire"], damageTypes.Single(damageType => damageType.Id == "Flame").Traits);
        Assert.Equal(100, damageTypes.Single(damageType => damageType.Id == "DirectDamage").PiercingRatio);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(101)]
    public void ADamageTypeWithoutAValidPiercingRatioFailsTheLoad(int? piercingRatio)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            GameCatalogDenormalizer.ReadDatamineDamageTypes(new Dictionary<string, DatamineDamageProfile>
            {
                ["Bolter"] = new(piercingRatio, null),
            }));

        Assert.Contains("'damage-types' damage type 'Bolter'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShippedUnitsResolveTheirFactionsAndDamageTypes()
    {
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateFactions(Snapshot.FactionViews, Snapshot.CharacterViews, Snapshot.MowList, Snapshot.NpcList, errors);
        GameCatalogValidator.ValidateDamageTypes(Snapshot.DamageTypeViews, Snapshot.CharacterViews, Snapshot.NpcList, errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void AnUnknownFactionOrAllianceMismatchFailsValidation()
    {
        var factions = Snapshot.FactionViews
            .Where(faction => faction.Id != "Orks")
            .Select(faction => faction.Id == "Necrons" ? faction with { Alliance = "Chaos" } : faction)
            .ToArray();
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateFactions(factions, Snapshot.CharacterViews, Snapshot.MowList, Snapshot.NpcList, errors);

        Assert.All(errors, error => Assert.Equal("InvalidFaction", error.Code));
        Assert.Contains(errors, error => error.Message.Contains("faction 'Orks', which is not a served faction", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Message.Contains("but its faction 'Necrons' is Chaos", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnservedDamageTypeFailsValidationOncePerType()
    {
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateDamageTypes(
            Snapshot.DamageTypeViews.Where(damageType => damageType.Id != "Bolter").ToArray(),
            Snapshot.CharacterViews,
            Snapshot.NpcList,
            errors);

        var error = Assert.Single(errors);
        Assert.Equal("InvalidDamageType", error.Code);
        Assert.Contains("damage type 'Bolter'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADuplicateFactionIdFailsValidation()
    {
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateFactions(
            [.. Snapshot.FactionViews, Snapshot.FactionViews[0]], [], [], [], errors);

        Assert.Contains(errors, error => error.Code == "DuplicateId" && error.Dataset == GameCatalogDatasets.Factions);
    }
}
