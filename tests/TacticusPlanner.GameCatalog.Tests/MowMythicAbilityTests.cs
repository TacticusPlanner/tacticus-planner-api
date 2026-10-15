using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class MowMythicAbilityTests
{
    private static GameCatalogMow Mow(string? mythicName) => new(
        "ultraDreadnought",
        "Galatian",
        "Mow",
        "Ultramarines",
        "Imperial",
        new GameCatalogMowAbility("DutyEternal", []),
        new GameCatalogMowAbility("MacroPlasmaIncinerator", []),
        mythicName is null ? null : new GameCatalogMowMythicAbility(mythicName));

    [Fact]
    public void EveryServedMowCarriesADistinctMythicAbility()
    {
        var mows = GameCatalogLoader.Load().MowList;

        Assert.Equal(11, mows.Count);
        Assert.All(mows, mow => Assert.False(string.IsNullOrWhiteSpace(mow.MythicAbility?.Name)));
        Assert.Equal(11, mows.Select(mow => mow.MythicAbility!.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("WisdomOfTheAncients", mows.Single(mow => mow.Id == "ultraDreadnought").MythicAbility!.Name);
    }

    [Fact]
    public void AnAbsentMythicAbilityIsValidAndOmittedFromJson()
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateMowMythicAbilities([Mow(null)], errors);

        Assert.Empty(errors);
        Assert.DoesNotContain("mythicAbility", System.Text.Json.JsonSerializer.Serialize(Mow(null), System.Text.Json.JsonSerializerOptions.Web));
    }

    [Theory]
    [InlineData("")]
    [InlineData("DutyEternal")]
    [InlineData("MacroPlasmaIncinerator")]
    public void AnEmptyOrRepeatedMythicAbilityFailsNamingTheMow(string name)
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateMowMythicAbilities([Mow(name)], errors);

        var error = Assert.Single(errors);
        Assert.Equal(GameCatalogDatasets.Mows, error.Dataset);
        Assert.Equal("InvalidMythicAbility", error.Code);
        Assert.Contains("ultraDreadnought", error.Message, StringComparison.Ordinal);
    }
}
