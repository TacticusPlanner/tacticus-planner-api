using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>
    /// Serves the raw <c>lre-clear-estimate</c> records as-is, except that each record's
    /// <c>unitCoefficients</c> is sorted by unit id (ordinal) so the hash does not depend on authoring order.
    /// </summary>
    public static IReadOnlyList<GameCatalogLreClearEstimate> BuildLreClearEstimate(IReadOnlyList<GameCatalogLreClearEstimate> raw) =>
        raw
            .Select(record => record with
            {
                UnitCoefficients = record.UnitCoefficients
                    .OrderBy(coefficient => coefficient.UnitId, StringComparer.Ordinal)
                    .ToArray(),
            })
            .ToArray();
}
