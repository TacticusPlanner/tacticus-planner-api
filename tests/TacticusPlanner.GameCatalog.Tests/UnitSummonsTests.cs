using System.Text.Json;
using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class UnitSummonsTests
{
    private static readonly JsonElement Table = JsonSerializer.SerializeToElement(new[] { 10, 20 });

    private static readonly Dictionary<string, DatamineAbility?> Abilities = new()
    {
        ["Summon"] = new(null, null, new Dictionary<string, JsonElement> { ["summonHp"] = Table, ["summonDmg"] = Table }),
        ["SecondSummon"] = new(null, null, new Dictionary<string, JsonElement> { ["summonHp_2"] = Table }),
        ["Strike"] = new(null, null, new Dictionary<string, JsonElement> { ["minDmg"] = Table }),
        ["Placeholder"] = null,
    };

    private static DatamineLineupUnit Unit(IReadOnlyList<string> actives, IReadOnlyList<string>? passives = null) =>
        new("Unit", "Ultramarines", "Imperial", "Rare", 3, null, new DatamineLineupStats(100, 20, 15), [], actives,
            passives, null, null, null);

    private static readonly Dictionary<string, DatamineLineupUnit> Lineup = new()
    {
        ["activeSummoner"] = Unit(["Summon"], ["Strike"]),
        ["passiveSummoner"] = Unit(["Strike"], ["SecondSummon"]),
        ["plain"] = Unit(["Strike", "Placeholder"]),
    };

    [Fact]
    public void ListedUnitsKeepTheirTypeAndEveryOtherUnitHasNone()
    {
        var summons = GameCatalogDenormalizer.ReadUnitSummons(
            new Dictionary<string, string> { ["activeSummoner"] = "active", ["passiveSummoner"] = "passive" },
            Lineup,
            Abilities);

        Assert.Equal(
            new Dictionary<string, string> { ["activeSummoner"] = "active", ["passiveSummoner"] = "passive", ["plain"] = "none" },
            summons);
    }

    [Theory]
    [InlineData("passiveSummoner", "passive", "'activeSummoner' has an ability with summon stats but no curated summons")]
    [InlineData("plain", "active", "'plain' is curated but has no ability with summon stats")]
    [InlineData("missing", "active", "'missing' is not a character or MoW")]
    [InlineData("activeSummoner", "sometimes", "'activeSummoner' has summons 'sometimes'")]
    public void ACurationThatDisagreesWithTheDatamineFailsTheLoad(string unitId, string type, string message)
    {
        var curated = new Dictionary<string, string> { ["activeSummoner"] = "active", ["passiveSummoner"] = "passive", [unitId] = type };
        if (unitId == "passiveSummoner")
        {
            curated.Remove("activeSummoner");
        }

        var error = Assert.Throws<InvalidOperationException>(() => GameCatalogDenormalizer.ReadUnitSummons(curated, Lineup, Abilities));

        Assert.Contains("'unit-summons'", error.Message, StringComparison.Ordinal);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShippedCatalogServesEachUnitsSummonType()
    {
        var catalog = GameCatalogLoader.Load();
        var characters = catalog.CharacterViews.ToDictionary(character => character.Id, character => character.Summons, StringComparer.Ordinal);
        var mows = catalog.MowList.ToDictionary(mow => mow.Id, mow => mow.Summons, StringComparer.Ordinal);

        Assert.Equal(("passive", "active", "none"), (characters["blackPossession"], characters["necroSpyder"], characters["ultraCalgar"]));
        Assert.Equal(("active", "none"), (mows["tyranBiovore"], mows["blackForgefiend"]));
        Assert.Equal(11, characters.Values.Count(summons => summons == "passive"));
        Assert.Equal(15, characters.Values.Count(summons => summons == "active"));
        Assert.Equal(6, mows.Values.Count(summons => summons == "active"));
        Assert.DoesNotContain("passive", mows.Values);
    }
}
