using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private static void ValidateShops(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors)
    {
        var unitOrMowIds = new HashSet<string>(
            snapshot.Characters.Select(character => character.Id), StringComparer.OrdinalIgnoreCase);
        unitOrMowIds.UnionWith(snapshot.Mows.Select(mow => mow.Id));

        ValidateShops(snapshot.RawShopsBySourceKey, snapshot.ShopViews, unitOrMowIds, errors);
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
            var shopId = GameCatalogDenormalizer.ShopId(sourceKey);

            for (var slotIndex = 0; slotIndex < rawShop.Products.Count; slotIndex++)
            {
                foreach (var variant in rawShop.Products[slotIndex])
                {
                    var owner = $"{shopId}[slot {slotIndex}]";

                    if (!ShopNormalization.TryParseTypedQuantity(variant.Reward, out _, out _))
                    {
                        errors.Add(new GameCatalogValidationError(
                            GameCatalogDatasets.Shops, "UnparseableReward",
                            $"'{owner}' has an unparseable reward string '{variant.Reward}'."));
                    }

                    if (variant.FreeOffer is not null
                        && !ShopNormalization.TryParseTypedQuantity(variant.FreeOffer, out _, out _))
                    {
                        errors.Add(new GameCatalogValidationError(
                            GameCatalogDatasets.Shops, "UnparseableFreeOffer",
                            $"'{owner}' has an unparseable freeOffer string '{variant.FreeOffer}'."));
                    }

                    if (!ShopNormalization.IsParseableCost(variant.Cost?.Type, variant.Cost?.Amount ?? double.NaN))
                    {
                        errors.Add(new GameCatalogValidationError(
                            GameCatalogDatasets.Shops, "UnparseableCost",
                            $"'{owner}' has a cost that is not a currency id and a non-negative amount."));
                    }
                }
            }
        }

        foreach (var shop in shopViews)
        {
            for (var slotIndex = 0; slotIndex < shop.Slots.Count; slotIndex++)
            {
                foreach (var variant in shop.Slots[slotIndex].Variants)
                {
                    var owner = $"{shop.Id}[slot {slotIndex}]";

                    if (variant.Days.Count == 0)
                    {
                        errors.Add(new GameCatalogValidationError(
                            GameCatalogDatasets.Shops, "EmptyDayList",
                            $"'{owner}' reward '{variant.Reward.Type}' reduces to no available day-of-week."));
                    }

                    var shardUnitId = ShopNormalization.ShardUnitId(variant.Reward.Type);
                    if (shardUnitId is null)
                    {
                        continue;
                    }

                    RequireReference(
                        GameCatalogDatasets.Shops, owner, "reward.unitId", variant.UnitId ?? shardUnitId, unitOrMowIds, errors);
                }
            }
        }
    }
}
