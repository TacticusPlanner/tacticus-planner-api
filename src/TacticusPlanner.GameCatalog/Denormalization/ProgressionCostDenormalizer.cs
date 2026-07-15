using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    public static IReadOnlyList<GameCatalogAscensionCostView> BuildAscensionCosts(
        IReadOnlyList<GameCatalogAscensionCost> costs) =>
        costs
            .Select(cost => new GameCatalogAscensionCostView(
                cost.Progression, cost.Shards, cost.MythicShards, cost.Orbs, cost.OrbRarity))
            .ToArray();

    public static IReadOnlyList<GameCatalogUnlockShardCostView> BuildUnlockShardCosts(
        IReadOnlyList<GameCatalogUnlockShardCost> costs) =>
        costs
            .Select(cost => new GameCatalogUnlockShardCostView(cost.Rarity, cost.Shards))
            .ToArray();
}
