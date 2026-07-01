using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    public static IReadOnlyList<GameCatalogNpc> BuildNpcs(IReadOnlyDictionary<string, GameCatalogFactionNpcs> npcsByFaction) =>
        npcsByFaction
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => pair.Value.Npcs)
            .ToArray();
}
