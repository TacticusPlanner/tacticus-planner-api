using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>
    /// Consolidates the four authored raw shop files into the served <c>shops</c> array — one record per
    /// shop, keyed by the id after the <c>shops-</c> source-key prefix (<c>guild</c> / <c>war</c> /
    /// <c>rogue-trader</c> / <c>crusade</c>). The slot/variant tree is preserved in source order; each
    /// variant's <c>reward</c>/<c>freeOffer</c> <c>"type:qty"</c> string is parsed to <c>{ type, qty }</c>,
    /// its <c>cost</c> to <c>{ currency, amount }</c>, its <c>maxPurchases</c> string to a number
    /// (absent ⇒ 1), and its Quartz <c>cronSchedule</c> to an explicit day-of-week list. Character-/mythic-
    /// shard rewards additionally carry the resolved <c>unitId</c>. Lock ids pass through verbatim and
    /// opaque. Parse failures are not thrown here — <c>Validation/ShopsValidation.cs</c> runs over the raw
    /// snapshot and fails the build with a proper error.
    /// </summary>
    public static IReadOnlyList<GameCatalogShopView> BuildShops(
        IReadOnlyDictionary<string, GameCatalogRawShop> rawShopsBySourceKey)
    {
        return GameCatalogDatasets.ShopSources
            .Where(rawShopsBySourceKey.ContainsKey)
            .Select(sourceKey => BuildShop(ShopId(sourceKey), rawShopsBySourceKey[sourceKey]))
            .ToArray();
    }

    /// <summary>The served shop id: the source dataset key with its <c>shops-</c> prefix removed.</summary>
    public static string ShopId(string sourceKey) =>
        sourceKey.StartsWith("shops-", StringComparison.Ordinal) ? sourceKey["shops-".Length..] : sourceKey;

    private static GameCatalogShopView BuildShop(string id, GameCatalogRawShop raw)
    {
        var slots = raw.Products
            .Select(variants => new GameCatalogShopSlotView(
                variants.Select(BuildVariant).ToArray()))
            .ToArray();

        var refreshCost = raw.RefreshCost is { } cost
            ? new GameCatalogShopRefreshCostView(cost.ResourceType, cost.Amount)
            : null;

        return new GameCatalogShopView(
            id, raw.DisplayLocation, raw.RefreshWithAdWatch, raw.AllowedRefreshesPerDay, refreshCost, slots);
    }

    private static GameCatalogShopVariantView BuildVariant(GameCatalogRawShopVariant raw)
    {
        var reward = ParseReward(raw.Reward);
        var freeOffer = raw.FreeOffer is null ? null : ParseReward(raw.FreeOffer);
        var conditions = raw.Conditions;

        return new GameCatalogShopVariantView(
            reward,
            ShopNormalization.ShardUnitId(reward.Type),
            freeOffer,
            ParseCost(raw.Cost),
            ShopNormalization.ParseMaxPurchasesPerDay(raw.MaxPurchases),
            raw.Weight,
            ShopNormalization.ReduceCronToDays(raw.CronSchedule),
            conditions?.MinPowerLevel,
            conditions?.MaxPowerLevel,
            conditions?.LockId);
    }

    private static GameCatalogShopCostView ParseCost(GameCatalogRawShopCost? raw) =>
        raw is not null
            ? new GameCatalogShopCostView(raw.Type, raw.Amount)
            // Omitted cost — ValidateShops reports UnparseableCost; emit a JSON-safe placeholder (empty
            // currency, -1 amount, never NaN) so the build reaches validation rather than throwing here,
            // mirroring ParseReward. The served views are hashed before validation runs.
            : new GameCatalogShopCostView(string.Empty, -1);

    private static GameCatalogShopRewardView ParseReward(string raw) =>
        ShopNormalization.TryParseTypedQuantity(raw, out var type, out var qty)
            ? new GameCatalogShopRewardView(type, qty)
            // Unparseable — ValidateShops reports it; emit a best-effort placeholder so the build reaches
            // validation rather than throwing here.
            : new GameCatalogShopRewardView(raw ?? string.Empty, 1);
}
