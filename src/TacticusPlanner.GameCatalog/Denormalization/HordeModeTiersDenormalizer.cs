using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>
    /// Serves the raw <c>horde-mode-tiers</c> records as-is, except that each record's <c>characters</c>
    /// is sorted by unit id (ordinal) so the hash does not depend on authoring order. A missing list is
    /// served empty so validation, not the loader, reports it. A character's <c>synergies</c> keep their
    /// authoring order (it ranks them); its <c>relics</c> are sorted by equipment id (ordinal), so the
    /// hash does not depend on their authoring order. An empty list is served as not set.
    /// </summary>
    public static IReadOnlyList<GameCatalogHordeModeTiers> BuildHordeModeTiers(IReadOnlyList<GameCatalogHordeModeTiers> raw) =>
        raw
            .Select(record => record with
            {
                Tiers = record.Tiers ?? [],
                Characters = (record.Characters ?? [])
                    .OrderBy(character => character.UnitId, StringComparer.Ordinal)
                    .Select(character => character with
                    {
                        Synergies = character.Synergies is { Count: > 0 } synergies ? synergies : null,
                        Relics = character.Relics is { Count: > 0 } relics
                            ? relics.OrderBy(relic => relic.EquipmentId, StringComparer.Ordinal).ToArray()
                            : null,
                    })
                    .ToArray(),
            })
            .ToArray();
}
