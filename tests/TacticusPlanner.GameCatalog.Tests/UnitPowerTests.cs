using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class UnitPowerTests
{
    private static readonly Lazy<GameCatalogSnapshot> Catalog = new(GameCatalogLoader.Load);

    private static GameCatalogSnapshot Snapshot => Catalog.Value;

    private static GameCatalogUnitPowerUnit Unit(GameCatalogUnitPower record, string unitId) =>
        Assert.Single(record.Units, unit => unit.UnitId == unitId);

    private static GameCatalogRawUnitPower WithLineup(string unitId, GameCatalogRawUnitPowerEntry? entry)
    {
        var lineup = new Dictionary<string, GameCatalogRawUnitPowerEntry>(Snapshot.UnitPower.Lineup, StringComparer.Ordinal);
        if (entry is null)
        {
            lineup.Remove(unitId);
        }
        else
        {
            lineup[unitId] = entry;
        }

        return Snapshot.UnitPower with { Lineup = lineup };
    }

    private static List<GameCatalogValidationError> Validate(
        GameCatalogRawUnitPower? raw = null,
        IReadOnlyList<GameCatalogCharacter>? characters = null,
        IReadOnlyList<GameCatalogRawNpc>? npcs = null,
        IReadOnlyList<GameCatalogCharacterAbilityCost>? abilityCosts = null,
        IReadOnlyList<GameCatalogEquipment>? equipment = null)
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateUnitPower(
            raw ?? Snapshot.UnitPower,
            characters ?? Snapshot.Characters,
            Snapshot.Mows,
            npcs ?? Snapshot.Npcs,
            Snapshot.Upgrades,
            abilityCosts ?? Snapshot.CharacterAbilityCosts,
            equipment ?? Snapshot.Equipment,
            errors);
        return errors;
    }

    private static void AssertSingleError(List<GameCatalogValidationError> errors, params string[] expectedFragments)
    {
        var error = Assert.Single(errors);
        Assert.Equal(GameCatalogDatasets.UnitPower, error.Dataset);
        Assert.Equal("InvalidUnitPower", error.Code);
        Assert.Contains("unit-power", error.Message, StringComparison.Ordinal);
        foreach (var fragment in expectedFragments)
        {
            Assert.Contains(fragment, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ServesTheCharacterShape()
    {
        var record = Assert.Single(Snapshot.UnitPowerViews);
        Assert.Equal("unit-power", record.Id);

        var tigurius = Unit(record, "ultraTigurius");
        Assert.Equal(100, tigurius.PowerMultiplier);
        Assert.Equal(20, tigurius.RankUpStatIncreases.Count);
        Assert.Equal([122, 122, 56, 56, 26, 26], tigurius.RankUpStatIncreases[12]);

        var gold1 = Assert.Single(Snapshot.Characters, character => character.Id == "ultraTigurius").RankUpUpgrades[12];
        Assert.Equal("Gold1", gold1.Rank);
        Assert.Equal(["upgHpL120C", "upgHpL011C", "upgDmgL005C", "upgDmgL004C", "upgArmL004C", "upgArmL010C"], gold1.UpgradeIds);

        Assert.Equal(new GameCatalogHeroPowerStep(9, 160, 5), record.HeroProgressionSteps[9]);
        Assert.Contains(new GameCatalogTraitPowerModifier("Psyker", 125), record.TraitPowerModifiers);
        Assert.Contains(new GameCatalogAbilityPowerModifier("StormOfWrath", 120), record.AbilityPowerModifiers);
        Assert.Contains(new GameCatalogDamageProfileModifier("Psychic", 191), record.DamageProfileModifiers);
    }

    [Fact]
    public void ServesTheMowShape()
    {
        var record = Assert.Single(Snapshot.UnitPowerViews);

        Assert.Empty(Unit(record, "tyranBiovore").RankUpStatIncreases);
        Assert.Equal(new GameCatalogMowPowerStep(19, 29, 150000), record.MowProgressionSteps[19]);
        Assert.Equal(0, record.MowProgressionSteps[0].MythicAbilityPower);
    }

    [Fact]
    public void ServesEveryCatalogUnitSortedWithEveryListSorted()
    {
        var record = Assert.Single(Snapshot.UnitPowerViews);
        var catalogIds = Snapshot.Characters.Select(character => character.Id)
            .Concat(Snapshot.Mows.Select(mow => mow.Id))
            .Order(StringComparer.Ordinal);

        Assert.Equal(catalogIds, record.Units.Select(unit => unit.UnitId));
        Assert.Equal(record.DamageProfileModifiers.Select(m => m.DamageType).Order(StringComparer.Ordinal), record.DamageProfileModifiers.Select(m => m.DamageType));
        Assert.Equal(record.TraitPowerModifiers.Select(m => m.TraitId).Order(StringComparer.Ordinal), record.TraitPowerModifiers.Select(m => m.TraitId));
        Assert.Equal(record.AbilityPowerModifiers.Select(m => m.AbilityId).Order(StringComparer.Ordinal), record.AbilityPowerModifiers.Select(m => m.AbilityId));
        Assert.Equal(Enumerable.Range(0, 20), record.HeroProgressionSteps.Select(step => step.Step));
        Assert.Equal(Enumerable.Range(0, 20), record.MowProgressionSteps.Select(step => step.Step));
    }

    [Fact]
    public void DatasetIsListedInTheManifestWithAHash()
    {
        var entry = Assert.Single(Snapshot.Manifest.Datasets, dataset => dataset.Key == GameCatalogDatasets.UnitPower);
        Assert.False(string.IsNullOrWhiteSpace(entry.Hash));
        Assert.Contains(GameCatalogDatasets.UnitPower, GameCatalogDatasets.Served);
    }

    [Fact]
    public void MissingPowerMultiplierDefaultsTo100AndRowsAreTruncatedToTheCatalogRanks()
    {
        var rows = Snapshot.UnitPower.Lineup["ultraTigurius"].UpgradesStatIncrease!;
        var raw = WithLineup("ultraTigurius", new GameCatalogRawUnitPowerEntry(null, [.. rows, [1, 2, 3, 4, 5, 6]]));

        var record = Assert.Single(GameCatalogDenormalizer.BuildUnitPower(raw, Snapshot.Characters, Snapshot.Mows));

        var tigurius = Unit(record, "ultraTigurius");
        Assert.Equal(100, tigurius.PowerMultiplier);
        Assert.Equal(20, tigurius.RankUpStatIncreases.Count);
    }

    [Fact]
    public void LineupUnitsTheCatalogDoesNotHaveAreSkipped()
    {
        var raw = WithLineup("unreleasedHero", new GameCatalogRawUnitPowerEntry(110, [[1, 2, 3, 4, 5, 6]]));

        var record = Assert.Single(GameCatalogDenormalizer.BuildUnitPower(raw, Snapshot.Characters, Snapshot.Mows));

        Assert.DoesNotContain(record.Units, unit => unit.UnitId == "unreleasedHero");
        Assert.Empty(Validate(raw));
    }

    [Fact]
    public void EmbeddedDataIsValid() => Assert.Empty(Validate());

    [Fact]
    public void RejectsACharacterWithoutALineupEntry()
    {
        var tigurius = Assert.Single(Snapshot.Characters, character => character.Id == "ultraTigurius");

        var errors = Validate(characters: [.. Snapshot.Characters, tigurius with { Id = "newHero" }]);

        AssertSingleError(errors, "newHero");
    }

    [Fact]
    public void RejectsAMisalignedStatIncreaseRow()
    {
        var rows = Snapshot.UnitPower.Lineup["ultraTigurius"].UpgradesStatIncrease!.ToArray();
        rows[0] = [1, 2, 3, 4, 5];

        var errors = Validate(WithLineup("ultraTigurius", new GameCatalogRawUnitPowerEntry(null, rows)));

        AssertSingleError(errors, "ultraTigurius", "Stone1");
    }

    [Fact]
    public void RejectsTooFewStatIncreaseRows()
    {
        var rows = Snapshot.UnitPower.Lineup["ultraTigurius"].UpgradesStatIncrease!.Take(19).ToArray();

        var errors = Validate(WithLineup("ultraTigurius", new GameCatalogRawUnitPowerEntry(null, rows)));

        AssertSingleError(errors, "ultraTigurius", "19 rows");
    }

    [Fact]
    public void RejectsANegativeValue()
    {
        var errors = Validate(WithLineup("ultraTigurius", Snapshot.UnitPower.Lineup["ultraTigurius"] with { PowerMultiplier = -1 }));

        AssertSingleError(errors, "ultraTigurius", "-1");
    }

    [Fact]
    public void RejectsAnUnknownDamageType()
    {
        var npc = Snapshot.Npcs[0] with { MeleeDamage = "Warp" };

        var errors = Validate(npcs: [.. Snapshot.Npcs, npc]);

        AssertSingleError(errors, "Warp");
    }

    [Fact]
    public void RejectsAnUpgradeThatRaisesNoPowerStat()
    {
        var tigurius = Assert.Single(Snapshot.Characters, character => character.Id == "ultraTigurius");
        var rankUps = tigurius.RankUpUpgrades.ToArray();
        rankUps[0] = rankUps[0] with { UpgradeIds = [.. rankUps[0].UpgradeIds.Take(5), "notAnUpgrade"] };

        var errors = Validate(characters: [.. Snapshot.Characters.Where(c => c.Id != "ultraTigurius"), tigurius with { RankUpUpgrades = rankUps }]);

        AssertSingleError(errors, "ultraTigurius", "notAnUpgrade");
    }

    [Fact]
    public void RejectsACurveShorterThanTheHighestAbilityLevel()
    {
        var curves = Snapshot.UnitPower.AbilityPowerCurve;
        var raw = Snapshot.UnitPower with { AbilityPowerCurve = curves with { Passive = curves.Passive.Take(59).ToArray() } };

        var errors = Validate(raw);

        AssertSingleError(errors, "passive", "60");
    }

    [Fact]
    public void RejectsARelicWithMoreLevelsThanTheRelicCurve()
    {
        var relic = Snapshot.Equipment.First(item => item.IsRelic);
        var longer = relic with { Id = "I_LongRelic", Levels = [.. relic.Levels, relic.Levels[^1]] };

        var errors = Validate(equipment: [.. Snapshot.Equipment, longer]);

        AssertSingleError(errors, "I_LongRelic");
    }

    [Fact]
    public void RejectsANonPositiveMultiplier()
    {
        var steps = Snapshot.UnitPower.HeroProgressionSteps.ToArray();
        steps[3] = steps[3] with { UnitStatMultiplierPct = 0 };

        var errors = Validate(Snapshot.UnitPower with { HeroProgressionSteps = steps });

        AssertSingleError(errors, "heroProgressionSteps", "'3'");
    }

    [Fact]
    public void RejectsTheWrongNumberOfProgressionSteps()
    {
        var raw = Snapshot.UnitPower with { HeroProgressionStepsMoW = Snapshot.UnitPower.HeroProgressionStepsMoW.Take(19).ToArray() };

        var errors = Validate(raw);

        AssertSingleError(errors, "heroProgressionStepsMoW", "19");
    }
}
