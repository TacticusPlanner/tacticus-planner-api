using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class UnitAbilitiesTests
{
    private static readonly Dictionary<string, DatamineAbility?> Abilities = new()
    {
        ["Strike"] = new(null, null, null, "Melee"),
        ["Volley"] = new(null, null, null, "Ranged"),
        ["Aura"] = new(null, null),
        ["Charge"] = new(null, null, null, "Normal"),
    };

    private static DatamineLineupUnit Unit(IReadOnlyList<string> actives, IReadOnlyList<string>? passives, params string[] traits) =>
        new("Unit", "Ultramarines", "Imperial", "Rare", 3, null, new DatamineLineupStats(100, 20, 15), traits, actives,
            passives, null, null, null);

    private static readonly Dictionary<string, DatamineLineupUnit> Lineup = new()
    {
        ["hero"] = Unit(["Strike"], ["Aura"]),
        ["mow"] = Unit(["Volley", "Charge"], null, "MachineOfWar"),
    };

    private static Dictionary<string, Dictionary<string, GameCatalogCuratedAbility>> Curated() => new()
    {
        ["hero"] = new()
        {
            ["Strike"] = new(["damage"], "single"),
            ["Aura"] = new(["support"], "aoe"),
        },
        ["mow"] = new()
        {
            ["Volley"] = new(["damage", "utility"], "aoe"),
            ["Charge"] = new(["utility"], "none"),
        },
    };

    [Fact]
    public void ServesEachUnitsAbilitiesInOrderWithTheirAttackRange()
    {
        var abilities = GameCatalogDenormalizer.ReadUnitAbilities(Curated(), Lineup, Abilities);

        Assert.Equal(
            [new("Strike", "active", ["damage"], "single", "melee"), new("Aura", "passive", ["support"], "aoe", null)],
            abilities["hero"],
            AbilityComparer.Instance);
        Assert.Equal(
            [new("Volley", "primary", ["damage", "utility"], "aoe", "ranged"), new("Charge", "secondary", ["utility"], "none", null)],
            abilities["mow"],
            AbilityComparer.Instance);
    }

    [Theory]
    [InlineData("missingUnit", "'mow' has no curated abilities")]
    [InlineData("missingAbility", "'hero' ability 'Aura' is not curated")]
    [InlineData("extraAbility", "'hero' ability 'Bolt' is curated but not one of its abilities")]
    [InlineData("unknownUnit", "'ghost' is not a character or MoW")]
    [InlineData("badRole", "'hero' ability 'Strike' has roles [healing]")]
    [InlineData("noRole", "'hero' ability 'Strike' has roles []")]
    [InlineData("badTarget", "'hero' ability 'Strike' has target 'line'")]
    public void ACurationThatDisagreesWithTheDatamineFailsTheLoad(string change, string message)
    {
        var curated = Curated();
        switch (change)
        {
            case "missingUnit":
                curated.Remove("mow");
                break;
            case "missingAbility":
                curated["hero"].Remove("Aura");
                break;
            case "extraAbility":
                curated["hero"]["Bolt"] = new(["damage"], "single");
                break;
            case "unknownUnit":
                curated["ghost"] = new() { ["Strike"] = new(["damage"], "single") };
                break;
            case "badRole":
                curated["hero"]["Strike"] = new(["healing"], "single");
                break;
            case "noRole":
                curated["hero"]["Strike"] = new([], "single");
                break;
            case "badTarget":
                curated["hero"]["Strike"] = new(["damage"], "line");
                break;
        }

        var error = Assert.Throws<InvalidOperationException>(() => GameCatalogDenormalizer.ReadUnitAbilities(curated, Lineup, Abilities));

        Assert.Contains("'unit-abilities'", error.Message, StringComparison.Ordinal);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShippedCatalogServesEveryUnitsAbilities()
    {
        var catalog = GameCatalogCompiler.Compile();
        var characters = catalog.CharacterViews.ToDictionary(character => character.Id, character => character.Abilities, StringComparer.Ordinal);
        var mows = catalog.MowList.ToDictionary(mow => mow.Id, mow => mow.Abilities, StringComparer.Ordinal);

        Assert.All(characters.Values, abilities => Assert.Equal(["active", "passive"], abilities.Select(ability => ability.Kind)));
        Assert.All(mows.Values, abilities => Assert.Equal(["primary", "secondary"], abilities.Select(ability => ability.Kind)));
        // Bellator's and Incisus' actives deal no damage (Severyn, 2026-11); Calgar's hits every enemy within 2 hexes.
        Assert.DoesNotContain("damage", characters["ultraInceptorSgt"][0].Roles);
        Assert.DoesNotContain("damage", characters["ultraApothecary"][0].Roles);
        var calgar = characters["ultraCalgar"][0];
        Assert.Equal(["damage"], calgar.Roles);
        Assert.Equal(("aoe", "ranged"), (calgar.Target, calgar.Attack));
    }

    private sealed class AbilityComparer : IEqualityComparer<GameCatalogUnitAbility>
    {
        public static readonly AbilityComparer Instance = new();

        public bool Equals(GameCatalogUnitAbility? x, GameCatalogUnitAbility? y) =>
            x is not null && y is not null && x.Id == y.Id && x.Kind == y.Kind && x.Roles.SequenceEqual(y.Roles)
            && x.Target == y.Target && x.Attack == y.Attack;

        public int GetHashCode(GameCatalogUnitAbility obj) => obj.Id.GetHashCode(StringComparison.Ordinal);
    }
}
