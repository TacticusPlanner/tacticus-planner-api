using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>
    /// Serves the raw <c>npc-tiers</c> records as-is, except that each record's <c>npcs</c> is sorted by NPC
    /// id (ordinal) so the hash does not depend on authoring order. Missing lists are served empty so
    /// validation, not the loader, reports them.
    /// </summary>
    public static IReadOnlyList<GameCatalogNpcTiers> BuildNpcTiers(IReadOnlyList<GameCatalogNpcTiers> raw) =>
        raw
            .Select(record => record with
            {
                Tiers = record.Tiers ?? [],
                Npcs = (record.Npcs ?? [])
                    .OrderBy(npc => npc.NpcId, StringComparer.Ordinal)
                    .ToArray(),
            })
            .ToArray();
}
