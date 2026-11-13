using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private static void ValidateShops(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors) =>
        ValidateShops(snapshot.RawShopsBySourceKey, snapshot.ShopViews, ShopUnitOrMowIds(snapshot), errors);

    /// <summary>Character and MoW ids a shard offer may name (case-insensitive, as the shard reward types are).</summary>
    private static HashSet<string> ShopUnitOrMowIds(GameCatalogSnapshot snapshot)
    {
        var unitOrMowIds = new HashSet<string>(
            snapshot.Characters.Select(character => character.Id), StringComparer.OrdinalIgnoreCase);
        unitOrMowIds.UnionWith(snapshot.Mows.Select(mow => mow.Id));
        return unitOrMowIds;
    }

    /// <summary>
    /// Takes the raw + served shop collections directly (not the whole snapshot) so it is unit-testable
    /// without constructing a <see cref="GameCatalogSnapshot"/>, mirroring <see cref="ValidateEvents"/>.
    /// Fails the build if any variant's <c>reward</c>/<c>freeOffer</c>/<c>cost</c> cannot be parsed, if a
    /// shard variant's resolved <c>unitId</c> does not resolve to a served character or MoW, or if any
    /// variant reduces to an empty day list.
    /// </summary>
    internal static void ValidateShops(
        IReadOnlyDictionary<string, GameCatalogRawShop> rawShopsBySourceKey,
        IReadOnlyList<GameCatalogShopView> shopViews,
        HashSet<string> unitOrMowIds,
        List<GameCatalogValidationError> errors)
    {
        foreach (var (sourceKey, rawShop) in rawShopsBySourceKey)
        {
            ValidateRawShopVariants(GameCatalogDatasets.Shops, GameCatalogDenormalizer.ShopId(sourceKey), rawShop, errors);
        }

        foreach (var shop in shopViews)
        {
            ValidateShopSlotViews(GameCatalogDatasets.Shops, shop.Id, shop.Slots, unitOrMowIds, errors);
        }
    }

    /// <summary>
    /// The raw-string rules every shop variant must meet (parseable reward, free offer and cost), shared by the
    /// daily <c>shops</c> and seasonal <c>event-shops</c> datasets. <paramref name="owner"/> names the shop in errors.
    /// </summary>
    internal static void ValidateRawShopVariants(
        string dataset, string owner, GameCatalogRawShop rawShop, List<GameCatalogValidationError> errors)
    {
        for (var slotIndex = 0; slotIndex < rawShop.Products.Count; slotIndex++)
        {
            foreach (var variant in rawShop.Products[slotIndex])
            {
                var where = $"{owner}[slot {slotIndex}]";

                if (!ShopNormalization.TryParseTypedQuantity(variant.Reward, out _, out _))
                {
                    errors.Add(new GameCatalogValidationError(
                        dataset, "UnparseableReward",
                        $"'{where}' has an unparseable reward string '{variant.Reward}'."));
                }

                if (variant.FreeOffer is not null
                    && !ShopNormalization.TryParseTypedQuantity(variant.FreeOffer, out _, out _))
                {
                    errors.Add(new GameCatalogValidationError(
                        dataset, "UnparseableFreeOffer",
                        $"'{where}' has an unparseable freeOffer string '{variant.FreeOffer}'."));
                }

                if (!ShopNormalization.IsParseableCost(variant.Cost?.Type, variant.Cost?.Amount ?? double.NaN))
                {
                    errors.Add(new GameCatalogValidationError(
                        dataset, "UnparseableCost",
                        $"'{where}' has a cost that is not a currency id and a non-negative amount."));
                }
            }
        }
    }

    /// <summary>
    /// The served-view rules every shop variant must meet (a non-empty day list, a shard offer's unit resolving to a
    /// served character or MoW), shared by the daily <c>shops</c> and seasonal <c>event-shops</c> datasets.
    /// </summary>
    internal static void ValidateShopSlotViews(
        string dataset,
        string owner,
        IReadOnlyList<GameCatalogShopSlotView> slots,
        HashSet<string> unitOrMowIds,
        List<GameCatalogValidationError> errors)
    {
        for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
        {
            foreach (var variant in slots[slotIndex].Variants)
            {
                var where = $"{owner}[slot {slotIndex}]";

                if (variant.Days.Count == 0)
                {
                    errors.Add(new GameCatalogValidationError(
                        dataset, "EmptyDayList",
                        $"'{where}' reward '{variant.Reward.Type}' reduces to no available day-of-week."));
                }

                var shardUnitId = ShopNormalization.ShardUnitId(variant.Reward.Type);
                if (shardUnitId is null)
                {
                    continue;
                }

                RequireReference(dataset, where, "reward.unitId", variant.UnitId ?? shardUnitId, unitOrMowIds, errors);
            }
        }
    }
}
