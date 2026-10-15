using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>
    /// Serves the raw <c>horde-mode-tiers</c> records as-is, except that each record's <c>characters</c>
    /// is sorted by unit id (ordinal) so the hash does not depend on authoring order. A missing list is
    /// served empty so validation, not the loader, reports it.
    /// </summary>
    public static IReadOnlyList<GameCatalogHordeModeTiers> BuildHordeModeTiers(IReadOnlyList<GameCatalogHordeModeTiers> raw) =>
        raw
            .Select(record => record with
            {
                Tiers = record.Tiers ?? [],
                Characters = (record.Characters ?? [])
                    .OrderBy(character => character.UnitId, StringComparer.Ordinal)
                    .ToArray(),
            })
            .ToArray();
}
