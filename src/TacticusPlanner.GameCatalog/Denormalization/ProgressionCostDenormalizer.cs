using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>Raw path of the datamine file the ascension-costs dataset is built from.</summary>
    public const string DatamineHeroProgressionStepsPath = "gameconfig/units/heroProgressionSteps.json";

    /// <summary>Raw path of the datamine file the unlock-shard-costs dataset is built from.</summary>
    public const string DatamineHeroConversionPath = "gameconfig/units/heroConversion.json";

    private const string AscensionOrbPrefix = "heroAscensionOrb";

    // The game's `stars` count (0-14) as the star half of a served "{Rarity}:{Stars}" progression.
    private static readonly string[] StarLabels =
    [
        "None", "OneStar", "TwoStars", "ThreeStars", "FourStars", "FiveStars",
        "RedOneStar", "RedTwoStars", "RedThreeStars", "RedFourStars", "RedFiveStars",
        "OneBlueStar", "TwoBlueStars", "ThreeBlueStars", "MythicWings",
    ];

    private static readonly string[] RarityOrder = ["Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythic"];

    /// <summary>
    /// Reads the datamine <c>heroProgressionSteps.json</c> (one row per ascension step, in ladder order) into the
    /// cost of reaching each step from the previous one. The first step's game cost is the unlock cost (served by
    /// unlock-shard-costs), so it is read as free. Each other row's <c>costs</c> may hold <c>shards</c>,
    /// <c>mythicShards</c> and at most one <c>heroAscensionOrb{Rarity}</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">A row has an unknown star count or cost key.</exception>
    public static IReadOnlyList<GameCatalogAscensionCost> ReadDatamineAscensionCosts(
        IReadOnlyList<DatamineHeroProgressionStep> rows) =>
        rows
            .Select((row, index) =>
            {
                var orbs = row.Costs.Keys
                    .Where(key => key.StartsWith(AscensionOrbPrefix, StringComparison.Ordinal)
                        && RarityOrder.Contains(key[AscensionOrbPrefix.Length..]))
                    .ToList();
                var unknown = row.Costs.Keys.Except(["shards", "mythicShards", .. orbs]).ToList();
                if (row.Stars < 0 || row.Stars >= StarLabels.Length || orbs.Count > 1 || unknown.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Dataset '{GameCatalogDatasets.AscensionCostsServed}' row {index} must have stars 0-{StarLabels.Length - 1} " +
                        $"and costs of shards, mythicShards and at most one {AscensionOrbPrefix}{{Rarity}}; " +
                        $"found stars {row.Stars} and [{string.Join(", ", row.Costs.Keys)}].");
                }

                var progression = $"{row.Rarity}:{StarLabels[row.Stars]}";
                return index == 0
                    ? new GameCatalogAscensionCost(progression, 0, 0, 0, null)
                    : new GameCatalogAscensionCost(
                        progression,
                        row.Costs.GetValueOrDefault("shards"),
                        row.Costs.GetValueOrDefault("mythicShards"),
                        orbs is [var orb] ? row.Costs[orb] : 0,
                        orbs is [var rarity] ? rarity[AscensionOrbPrefix.Length..] : null);
            })
            .ToArray();

    /// <summary>
    /// Reads the datamine <c>heroConversion.json</c> (starting rarity -> shards to unlock) into one record per
    /// rarity, in rarity order. The game has no Mythic entry because no character starts at Mythic.
    /// </summary>
    /// <exception cref="InvalidOperationException">A key is not a known rarity.</exception>
    public static IReadOnlyList<GameCatalogUnlockShardCost> ReadDatamineUnlockShardCosts(
        IReadOnlyDictionary<string, DatamineHeroConversion> rows)
    {
        var unknown = rows.Keys.Except(RarityOrder).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                $"Dataset '{GameCatalogDatasets.UnlockShardCostsServed}' has unknown rarities [{string.Join(", ", unknown)}].");
        }

        return RarityOrder
            .Where(rows.ContainsKey)
            .Select(rarity => new GameCatalogUnlockShardCost(rarity, rows[rarity].Shards))
            .ToArray();
    }

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
