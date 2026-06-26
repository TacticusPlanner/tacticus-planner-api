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
