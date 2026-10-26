using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    // A MoW's source record carries no faction/alliance/kind (those live on the parent faction), so enrich
    // each mow with its faction context here — mirroring how characters are projected.
    public const string MowUnitKind = "Mow";

    public static IReadOnlyList<GameCatalogMow> BuildMows(
        IReadOnlyDictionary<string, GameCatalogFactionUnits> unitsByFaction) =>
        unitsByFaction
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => pair.Value.Mows.Select(mow => mow with
            {
                UnitKind = MowUnitKind,
                Faction = pair.Value.FactionId,
                Alliance = pair.Value.Alliance,
            }))
            .ToArray();

    /// <summary>Raw path of the datamine file the mow-upgrade-costs dataset is built from.</summary>
    public const string DatamineAbilityUpgradeCostsMoWPath = "gameconfig/units/abilityUpgradeCostsMoW.json";

    private const string ForgeBadgePrefix = "itemAscensionResource_";

    /// <summary>
    /// Reads the datamine <c>abilityUpgradeCostsMoW.json</c> (one row per MoW ability level) into cost rungs:
    /// <c>gold</c>, <c>dust</c> (salvage, absent = 0), <c>machinesOfWarToken</c> (components), exactly one
    /// <c>abilityToken{Rarity}</c> (badges) and at most one <c>itemAscensionResource_{Rarity}</c> (forge badges).
    /// </summary>
    /// <exception cref="InvalidOperationException">A row is missing a required key or has an unknown one.</exception>
    public static IReadOnlyList<GameCatalogMowUpgradeCost> ReadDatamineMowUpgradeCosts(
        IReadOnlyList<IReadOnlyDictionary<string, int>> rows) =>
        rows.Select((row, index) =>
        {
            var badges = row.Keys.Where(key => key.StartsWith(AbilityTokenPrefix, StringComparison.Ordinal)).ToList();
            var forgeBadges = row.Keys.Where(key => key.StartsWith(ForgeBadgePrefix, StringComparison.Ordinal)).ToList();
            var unknown = row.Keys.Except(["gold", "dust", "machinesOfWarToken", .. badges, .. forgeBadges]).ToList();
            if (!row.TryGetValue("gold", out var gold) || !row.TryGetValue("machinesOfWarToken", out var components)
                || badges.Count != 1 || forgeBadges.Count > 1 || unknown.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Dataset '{GameCatalogDatasets.MowUpgradeCostsServed}' row {index} must have gold, machinesOfWarToken, " +
                    $"exactly one abilityToken{{Rarity}}, at most one itemAscensionResource_{{Rarity}} and optional dust; " +
                    $"found [{string.Join(", ", row.Keys)}].");
            }

            return new GameCatalogMowUpgradeCost(
                gold,
                row.GetValueOrDefault("dust"),
                new GameCatalogAmountByRarity(badges[0][AbilityTokenPrefix.Length..], row[badges[0]]),
                forgeBadges is [var forge] ? new GameCatalogAmountByRarity(forge[ForgeBadgePrefix.Length..], row[forge]) : null,
                components);
        }).ToArray();

    // Project the raw mow upgrade-cost ladder into level-keyed rungs: the flat array's nth entry (0-based)
    // is the cost to raise a MoW ability to level n + 2 (level 1 is the starting level, so it has no cost).
    public static IReadOnlyList<GameCatalogMowUpgradeCostView> BuildMowUpgradeCosts(
        IReadOnlyList<GameCatalogMowUpgradeCost> costs) =>
        costs
            .Select((cost, index) => new GameCatalogMowUpgradeCostView(
                index + 2,
                cost.Gold,
                cost.Salvage,
                cost.Badges,
                cost.ForgeBadges,
                cost.Components))
            .ToArray();
}
