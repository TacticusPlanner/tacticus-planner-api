using System.Text.Json;
using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class GlobalValuesDenormalizerTests
{
    private static readonly DatamineGlobalValues Values = new(
        MaxStamina: 60,
        StaminaRegenerationTime: 300,
        StaminaRegenerationAmount: 1,
        WavesMaxStamina: 3,
        WavesStaminaRegenerationTime: 57600,
        WavesStaminaRegenerationAmount: 1,
        CampaignLevelMaxStamina: 3,
        CampaignLevelStaminaRegenerationTime: 86400,
        CampaignLevelStaminaRegenerationAmount: 3,
        MinUnitsNeededForBattle: 3,
        MaxUnitsPossibleForBattle: 5,
        TerrifyingBonus: -30,
        BattleFatigueFleeChance: 10,
        BattleFatigueFleeChanceOverkill: 40,
        TraitVariables: new Dictionary<string, JsonElement> { ["livingMetalHp"] = JsonSerializer.SerializeToElement(10) });

    private static Dictionary<string, JsonElement> Products(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private static readonly Dictionary<string, JsonElement> StaminaProduct = Products(
        """{ "product_stamina_00": { "rewards": ["stamina:60", "raidTicket:10"], "cost": { "type": "gems", "amount": 25 } } }""");

    [Fact]
    public void TheShippedValuesAreTheGames()
    {
        var values = Assert.Single(GameCatalogLoader.Load().GlobalValuesViews);

        Assert.Equal(60, values.MaxStamina);
        Assert.Equal(300, values.StaminaRegenerationTime);
        Assert.Equal((3, 57600), (values.WavesMaxStamina, values.WavesStaminaRegenerationTime));
        Assert.Equal((3, 86400, 3), (values.CampaignLevelMaxStamina, values.CampaignLevelStaminaRegenerationTime, values.CampaignLevelStaminaRegenerationAmount));
        Assert.Equal((3, 5), (values.MinUnitsNeededForBattle, values.MaxUnitsPossibleForBattle));
        Assert.Equal((-30, 10, 40), (values.TerrifyingBonus, values.BattleFatigueFleeChance, values.BattleFatigueFleeChanceOverkill));
        Assert.Equal(50, values.TraitVariables["deathToTheFalseEmperorExtraHitChance"].GetInt32());
        Assert.Equal("DeathGuard", values.TraitVariables["putridExplosionFactionId"].GetString());
        Assert.Equal(8, values.StaminaShopTiers.Count);
        Assert.Equal(new GameCatalogStaminaShopTier("product_stamina_00", 60, 10, 25), values.StaminaShopTiers[0]);
        Assert.Equal(new GameCatalogStaminaShopTier("product_stamina_06", 100, 15, 1500), values.StaminaShopTiers[^1]);
    }

    [Fact]
    public void ATierWithoutRaidTicketsServesZero()
    {
        var values = GameCatalogDenormalizer.ReadDatamineGlobalValues(
            Values,
            ["product_stamina_x"],
            Products("""{ "product_stamina_x": { "rewards": ["stamina:100"], "cost": { "type": "gems", "amount": 50 } } }"""));

        Assert.Equal(new GameCatalogStaminaShopTier("product_stamina_x", 100, 0, 50), Assert.Single(values).StaminaShopTiers[0]);
    }

    public static TheoryData<DatamineGlobalValues, string> InvalidValues() => new()
    {
        { Values with { MaxStamina = null }, "'maxStamina' must be positive" },
        { Values with { StaminaRegenerationTime = 0 }, "'staminaRegenerationTime' must be positive" },
        { Values with { MaxUnitsPossibleForBattle = 2 }, "'maxUnitsPossibleForBattle' 2 is below" },
        { Values with { TerrifyingBonus = null }, "'terrifyingBonus' is missing" },
        { Values with { BattleFatigueFleeChanceOverkill = 101 }, "'battleFatigueFleeChanceOverkill' must be 0-100" },
        { Values with { TraitVariables = null }, "'traitVariables' is missing" },
    };

    [Theory]
    [MemberData(nameof(InvalidValues))]
    public void AMissingOrOutOfRangeValueFailsTheLoad(DatamineGlobalValues values, string expected)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            GameCatalogDenormalizer.ReadDatamineGlobalValues(values, ["product_stamina_00"], StaminaProduct));

        Assert.Contains("'global-values'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "rewards": ["stamina:60"], "cost": { "type": "gold", "amount": 25 } }""", "must be a products.json product")]
    [InlineData("""{ "rewards": ["stamina:60", "gems:5"], "cost": { "type": "gems", "amount": 25 } }""", "has reward 'gems:5'")]
    [InlineData("""{ "rewards": ["raidTicket:10"], "cost": { "type": "gems", "amount": 25 } }""", "gives no stamina")]
    public void AnUnmappableShopTierFailsTheLoad(string product, string expected)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            GameCatalogDenormalizer.ReadDatamineGlobalValues(Values, ["p"], Products($$"""{ "p": {{product}} }""")));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingShopProductOrLadderFailsTheLoad()
    {
        Assert.Contains("'nope'", Assert.Throws<InvalidOperationException>(() =>
            GameCatalogDenormalizer.ReadDatamineGlobalValues(Values, ["nope"], StaminaProduct)).Message, StringComparison.Ordinal);
        Assert.Contains("no tiers", Assert.Throws<InvalidOperationException>(() =>
            GameCatalogDenormalizer.ReadDatamineGlobalValues(Values, [], StaminaProduct)).Message, StringComparison.Ordinal);
    }
}
