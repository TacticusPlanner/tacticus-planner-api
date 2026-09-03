using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Utils;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class ShopsDenormalizerTests
{
    private static GameCatalogRawShopVariant Variant(
        string reward,
        string cron = "0 0 0 ? * * *",
        string? maxPurchases = null,
        string? freeOffer = null,
        double? weight = 1,
        GameCatalogRawShopConditions? conditions = null,
        GameCatalogRawShopCost? cost = null) =>
        new(weight, conditions ?? new GameCatalogRawShopConditions(null, null, null), cron, reward, freeOffer, maxPurchases,
            cost ?? new GameCatalogRawShopCost("guildCredits", 525));

    private static GameCatalogRawShop Shop(params GameCatalogRawShopVariant[][] slots) =>
        new("guildMerchant", new GameCatalogRawShopRefreshCost("gems", 50), true, 1, slots);

    private static GameCatalogShopView BuildOne(GameCatalogRawShop shop) =>
        Assert.Single(GameCatalogDenormalizer.BuildShops(new Dictionary<string, GameCatalogRawShop> { ["shops-guild"] = shop }));

    [Fact]
    public void OneRecordPerSourceFileKeyedByTheIdAfterTheShopsPrefix()
    {
        var shops = GameCatalogDenormalizer.BuildShops(new Dictionary<string, GameCatalogRawShop>
        {
            ["shops-guild"] = Shop([Variant("gold:1000")]),
            ["shops-rogue-trader"] = Shop([Variant("gold:1000")]),
        });

        Assert.Equal(["guild", "rogue-trader"], shops.Select(shop => shop.Id));
    }

    [Fact]
    public void ParsesRewardFreeOfferCostAndDefaultsThePurchaseCap()
    {
        var view = BuildOne(Shop([
            Variant("shards_eldarFarseer:5", freeOffer: "draft_machinesOfWarTokens:10", maxPurchases: null,
                cost: new GameCatalogRawShopCost("guildCredits", 525)),
        ]));

        var variant = view.Slots[0].Variants[0];
        Assert.Equal(new GameCatalogShopRewardView("shards_eldarFarseer", 5), variant.Reward);
        Assert.Equal(new GameCatalogShopRewardView("draft_machinesOfWarTokens", 10), variant.FreeOffer);
        Assert.Equal(new GameCatalogShopCostView("guildCredits", 525), variant.Cost);
        Assert.Equal(1, variant.MaxPurchasesPerDay);
    }

    [Fact]
    public void AnOmittedCostBecomesAJsonSafeInvalidPlaceholderRatherThanThrowing()
    {
        // A source file that omits `cost` deserializes to a null Cost — BuildShops must not NRE; it emits an
        // unparseable placeholder (empty currency, -1 amount) that Validation/ShopsValidation.cs then rejects.
        var view = BuildOne(Shop([
            new GameCatalogRawShopVariant(
                1, new GameCatalogRawShopConditions(null, null, null), "0 0 0 ? * * *", "gold:1000", null, null, null),
        ]));

        var cost = view.Slots[0].Variants[0].Cost;
        Assert.Equal(string.Empty, cost.Currency);
        Assert.Equal(-1, cost.Amount);
        Assert.False(ShopNormalization.IsParseableCost(cost.Currency, cost.Amount));
    }

    [Fact]
    public void RewardWithNoExplicitQuantityBecomesQuantityOne()
    {
        var variant = BuildOne(Shop([Variant("itemAscensionResource_Mythic")])).Slots[0].Variants[0];

        Assert.Equal(new GameCatalogShopRewardView("itemAscensionResource_Mythic", 1), variant.Reward);
        Assert.Null(variant.FreeOffer);
    }

    [Fact]
    public void ExplicitPurchaseCapIsPreserved()
    {
        var variant = BuildOne(Shop([Variant("gold:1000", maxPurchases: "3")])).Slots[0].Variants[0];

        Assert.Equal(3, variant.MaxPurchasesPerDay);
    }

    [Fact]
    public void ReducesTheCronToAnExplicitDayOfWeekList()
    {
        var view = BuildOne(Shop([
            Variant("shards_spaceWulfen:5", cron: "0 0 0 ? * MON,THU *"),
            Variant("gold:1000", cron: "0 0 0 ? * * *"),
        ]));

        Assert.Equal(["MON", "THU"], view.Slots[0].Variants[0].Days);
        Assert.Equal(["MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"], view.Slots[0].Variants[1].Days);
    }

    [Fact]
    public void ShardRewardsCarryTheResolvedUnitIdAndNonShardRewardsDoNot()
    {
        var view = BuildOne(Shop(
            [Variant("shards_eldarFarseer:5")],
            [Variant("mythicShards_eldarLhykhis:3")],
            [Variant("upgHpM001")]));

        Assert.Equal("eldarFarseer", view.Slots[0].Variants[0].UnitId);
        Assert.Equal("eldarLhykhis", view.Slots[1].Variants[0].UnitId);
        Assert.Null(view.Slots[2].Variants[0].UnitId);
    }

    [Fact]
    public void PassesWeightPowerLevelBoundsAndLockIdThroughUnchanged()
    {
        var variant = BuildOne(Shop([
            Variant("shards_eldarLhykhis:5", weight: 2,
                conditions: new GameCatalogRawShopConditions(11, 20, "lock_crusade_shop_owns_unit_at_mythic")),
        ])).Slots[0].Variants[0];

        Assert.Equal(2, variant.Weight);
        Assert.Equal(11, variant.MinPowerLevel);
        Assert.Equal(20, variant.MaxPowerLevel);
        Assert.Equal("lock_crusade_shop_owns_unit_at_mythic", variant.LockId);
    }

    [Fact]
    public void LeavesOptionalConditionFieldsUnsetWhenTheSourceOmitsThem()
    {
        var variant = BuildOne(Shop([Variant("gold:1000", weight: null)])).Slots[0].Variants[0];

        Assert.Null(variant.Weight);
        Assert.Null(variant.MinPowerLevel);
        Assert.Null(variant.MaxPowerLevel);
        Assert.Null(variant.LockId);
    }
}
