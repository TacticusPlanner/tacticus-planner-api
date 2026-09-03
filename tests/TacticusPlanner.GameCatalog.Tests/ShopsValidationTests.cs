using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class ShopsValidationTests
{
    private static readonly HashSet<string> KnownUnits =
        new(StringComparer.OrdinalIgnoreCase) { "eldarFarseer", "eldarLhykhis" };

    private static GameCatalogRawShopVariant Variant(
        string reward, string? freeOffer = null, GameCatalogRawShopCost? cost = null, string cron = "0 0 0 ? * * *") =>
        new(1, new GameCatalogRawShopConditions(null, null, null), cron, reward, freeOffer, null,
            cost ?? new GameCatalogRawShopCost("guildCredits", 525));

    private static (IReadOnlyDictionary<string, GameCatalogRawShop> Raw, IReadOnlyList<GameCatalogShopView> Views)
        Build(params GameCatalogRawShopVariant[] variants)
    {
        var raw = new Dictionary<string, GameCatalogRawShop>
        {
            ["shops-guild"] = new("guildMerchant", null, false, 0, [variants]),
        };

        return (raw, GameCatalogDenormalizer.BuildShops(raw));
    }

    private static List<GameCatalogValidationError> Validate(
        (IReadOnlyDictionary<string, GameCatalogRawShop> Raw, IReadOnlyList<GameCatalogShopView> Views) catalog)
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateShops(catalog.Raw, catalog.Views, KnownUnits, errors);
        return errors;
    }

    [Fact]
    public void CleanShopDataProducesNoErrors()
    {
        var errors = Validate(Build(
            Variant("shards_eldarFarseer:5"),
            Variant("gold:1000", freeOffer: "draft_machinesOfWarTokens:10"),
            Variant("upgHpM001", cron: "0 0 0 ? * MON,THU *")));

        Assert.Empty(errors);
    }

    [Fact]
    public void UnparseableRewardFailsValidation()
    {
        var errors = Validate(Build(Variant("shards_x:notANumber")));

        Assert.Contains(errors, error => error.Code == "UnparseableReward");
    }

    [Fact]
    public void UnparseableFreeOfferFailsValidation()
    {
        var errors = Validate(Build(Variant("gold:1000", freeOffer: "draft_x:")));

        Assert.Contains(errors, error => error.Code == "UnparseableFreeOffer");
    }

    [Fact]
    public void UnparseableCostFailsValidation()
    {
        var errors = Validate(Build(Variant("gold:1000", cost: new GameCatalogRawShopCost("", 525))));

        Assert.Contains(errors, error => error.Code == "UnparseableCost");
    }

    [Fact]
    public void VariantThatReducesToNoAvailableDayFailsValidation()
    {
        var errors = Validate(Build(Variant("gold:1000", cron: "0 0 0 ? * NOPE *")));

        Assert.Contains(errors, error => error.Code == "EmptyDayList");
    }

    [Fact]
    public void ShardOfferWhoseUnitIdDoesNotResolveFailsValidation()
    {
        var errors = Validate(Build(Variant("shards_notACharacter:5")));

        Assert.Contains(errors, error => error.Code == "MissingReference");
    }

    [Fact]
    public void ShardOfferWhoseUnitIdResolvesToAServedUnitPasses()
    {
        var errors = Validate(Build(Variant("mythicShards_eldarLhykhis:3")));

        Assert.DoesNotContain(errors, error => error.Code == "MissingReference");
    }
}
