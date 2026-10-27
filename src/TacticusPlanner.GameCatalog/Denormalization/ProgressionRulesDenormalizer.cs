using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameDomain;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>Raw path of the datamine XP curve.</summary>
    public const string DatamineXpLevelsPath = "gameconfig/units/xpLevels.json";

    /// <summary>Raw path of the datamine rank-up slot XP requirements.</summary>
    public const string DatamineUpgradeSlotsPath = "gameconfig/units/upgradeSlots.json";

    /// <summary>Raw path of the datamine consumables (XP books).</summary>
    public const string DatamineConsumablesPath = "gameconfig/consumables.json";

    private const int UpgradeSlotsPerRank = 6;

    /// <summary>
    /// Builds the single progression-rules record. The XP curve must be positive and strictly increasing; there must be
    /// one XP book per rarity; each rank row must hold six XP levels, one row per rank from Stone1 up to the one below
    /// the highest; and both ascension ladders must walk exactly the catalog progression order with a known max rank.
    /// </summary>
    /// <exception cref="InvalidOperationException">A table is missing, misshapen or out of order.</exception>
    public static IReadOnlyList<GameCatalogProgressionRules> ReadDatamineProgressionRules(
        IReadOnlyList<int> xpLevels,
        DatamineConsumables consumables,
        DatamineUpgradeSlots upgradeSlots,
        IReadOnlyList<DatamineHeroProgressionRule> heroSteps,
        IReadOnlyList<DatamineMowProgressionRule> mowSteps)
    {
        if (xpLevels.Count == 0 || xpLevels[0] <= 0 || xpLevels.Zip(xpLevels.Skip(1)).Any(pair => pair.Second <= pair.First))
        {
            throw ProgressionRulesError("xpLevels must be positive and strictly increasing.");
        }

        var books = consumables.XpBooks ?? throw ProgressionRulesError("consumables.json has no xpBooks.");
        if (books.Any(book => string.IsNullOrEmpty(book.Id) || book.XpIncrease <= 0 || book.Gold <= 0)
            || !books.Select(book => book.Rarity).Order(StringComparer.Ordinal).SequenceEqual(RarityOrder.Order(StringComparer.Ordinal)))
        {
            throw ProgressionRulesError(
                $"xpBooks must be one book per rarity ({string.Join(", ", RarityOrder)}) with an id and positive XP and gold.");
        }

        var rankRows = upgradeSlots.XpLevelRequirements ?? throw ProgressionRulesError("upgradeSlots.json has no xpLevelRequirements.");
        var lastRank = Enum.GetValues<UnitRank>().Max();
        if (rankRows.Count != (int)lastRank || rankRows.Any(row => row.Count != UpgradeSlotsPerRank || row.Any(level => level <= 0)))
        {
            throw ProgressionRulesError(
                $"upgradeSlots.xpLevelRequirements must have {(int)lastRank} rows (Stone1 to {lastRank - 1}) of {UpgradeSlotsPerRank} positive levels; " +
                $"found {rankRows.Count} rows.");
        }

        var heroRules = heroSteps.Select((row, step) =>
        {
            if (row.MaxRank < 0 || row.MaxRank > (int)lastRank || row.MaxXpLevel <= 0)
            {
                throw ProgressionRulesError($"heroProgressionSteps row {step} has maxRank {row.MaxRank} and maxXpLevel {row.MaxXpLevel}.");
            }

            return new GameCatalogHeroProgressionRule(
                step,
                StepProgression("heroProgressionSteps", step, row.Rarity, row.Stars),
                row.Rarity,
                row.Stars,
                row.MaxXpLevel,
                ((UnitRank)row.MaxRank).ToString(),
                row.UnitStatMultiplierPct,
                row.AbilityStatMultiplierPct,
                row.AscensionCosts is { } costs && costs.TryGetValue("hreShards", out var hreShards) ? hreShards : null);
        }).ToArray();

        var mowRules = mowSteps.Select((row, step) => new GameCatalogMowProgressionRule(
            step,
            StepProgression("heroProgressionStepsMoW", step, row.Rarity, row.Stars),
            row.Rarity,
            row.Stars,
            row.MaxAbilityLevel > 0
                ? row.MaxAbilityLevel
                : throw ProgressionRulesError($"heroProgressionStepsMoW row {step} has maxAbilityLevel {row.MaxAbilityLevel}."),
            row.AbilityStatMultiplierPct,
            row.MythicAbilityStatMultiplierPct)).ToArray();

        foreach (var (file, progressions) in new[]
                 {
                     ("heroProgressionSteps", heroRules.Select(rule => rule.Progression)),
                     ("heroProgressionStepsMoW", mowRules.Select(rule => rule.Progression)),
                 })
        {
            if (!progressions.SequenceEqual(ProgressionRules.ProgressionOrder))
            {
                throw ProgressionRulesError($"{file} must walk exactly [{string.Join(", ", ProgressionRules.ProgressionOrder)}].");
            }
        }

        return
        [
            new GameCatalogProgressionRules(
                xpLevels.ToArray(),
                books
                    .OrderBy(book => Array.IndexOf(RarityOrder, book.Rarity))
                    .Select(book => new GameCatalogXpBook(book.Id!, book.Rarity!, book.XpIncrease, book.Gold))
                    .ToArray(),
                rankRows
                    .Select((row, rank) => new GameCatalogUpgradeSlotRank(((UnitRank)rank).ToString(), row.ToArray()))
                    .ToArray(),
                heroRules,
                mowRules),
        ];
    }

    private static string StepProgression(string file, int step, string rarity, int stars) =>
        stars >= 0 && stars < StarLabels.Length && RarityOrder.Contains(rarity)
            ? $"{rarity}:{StarLabels[stars]}"
            : throw ProgressionRulesError($"{file} row {step} has rarity '{rarity}' and stars {stars}.");

    private static InvalidOperationException ProgressionRulesError(string message) =>
        new($"Dataset '{GameCatalogDatasets.ProgressionRules}': {message}");
}
