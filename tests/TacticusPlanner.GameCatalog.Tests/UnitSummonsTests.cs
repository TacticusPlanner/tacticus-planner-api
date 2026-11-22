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
        ["optionalSummoner"] = Unit(["Summon"], ["Strike"]),
        ["forcedSummoner"] = Unit(["Strike"], ["SecondSummon"]),
        ["conditionalSummoner"] = Unit(["Strike"], ["Summon"]),
        ["plain"] = Unit(["Strike", "Placeholder"]),
    };

    [Fact]
    public void ListedUnitsKeepTheirTypeAndEveryOtherUnitHasNone()
    {
        var summons = GameCatalogDenormalizer.ReadUnitSummons(
            new Dictionary<string, string>
            {
                ["optionalSummoner"] = "optional",
                ["conditionalSummoner"] = "conditional",
                ["forcedSummoner"] = "forced",
            },
            Lineup,
            Abilities);

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["optionalSummoner"] = "optional",
                ["conditionalSummoner"] = "conditional",
                ["forcedSummoner"] = "forced",
                ["plain"] = "none",
            },
            summons);
    }

    [Theory]
    [InlineData("forcedSummoner", "forced", "'optionalSummoner' has an ability with summon stats but no curated summons")]
    [InlineData("plain", "optional", "'plain' is curated but has no ability with summon stats")]
    [InlineData("missing", "optional", "'missing' is not a character or MoW")]
    [InlineData("optionalSummoner", "sometimes", "'optionalSummoner' has summons 'sometimes'")]
    public void ACurationThatDisagreesWithTheDatamineFailsTheLoad(string unitId, string type, string message)
    {
        var curated = new Dictionary<string, string>
        {
            ["optionalSummoner"] = "optional",
            ["conditionalSummoner"] = "conditional",
            ["forcedSummoner"] = "forced",
            [unitId] = type,
        };
        if (unitId == "forcedSummoner")
        {
            curated.Remove("optionalSummoner");
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

        Assert.Equal(("forced", "optional", "conditional", "conditional", "none"),
            (characters["blackPossession"], characters["necroSpyder"], characters["admecMarshall"], characters["votanIronmaster"],
                characters["ultraCalgar"]));
        Assert.Equal(("optional", "none"), (mows["tyranBiovore"], mows["blackForgefiend"]));
        Assert.Equal(9, characters.Values.Count(summons => summons == "forced"));
        Assert.Equal(15, characters.Values.Count(summons => summons == "optional"));
        Assert.Equal(2, characters.Values.Count(summons => summons == "conditional"));
        Assert.Equal(6, mows.Values.Count(summons => summons == "optional"));
        Assert.DoesNotContain("forced", mows.Values);
    }
}
