using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>
    /// Serves the raw <c>horde-mode-tiers</c> records as-is, except that each record's <c>characters</c>
    /// is sorted by unit id (ordinal) so the hash does not depend on authoring order. A missing list is
    /// served empty so validation, not the loader, reports it. A character's <c>synergies</c> keep their
    /// authoring order (it ranks them); an empty list is served as not set.
    /// </summary>
    public static IReadOnlyList<GameCatalogHordeModeTiers> BuildHordeModeTiers(IReadOnlyList<GameCatalogHordeModeTiers> raw) =>
        raw
            .Select(record => record with
            {
                Tiers = record.Tiers ?? [],
                Characters = (record.Characters ?? [])
                    .OrderBy(character => character.UnitId, StringComparer.Ordinal)
                    .Select(character => character.Synergies is { Count: 0 } ? character with { Synergies = null } : character)
                    .ToArray(),
            })
            .ToArray();
}
