using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private static void ValidateNpcs(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors)
    {
        ValidateNpcs(snapshot.NpcsByFaction, errors);
    }

    /// <summary>
    /// Takes the raw per-faction NPC files directly (not the whole snapshot) so it is unit-testable,
    /// mirroring <see cref="ValidateShops"/>. The served <c>kind</c> is derived by an ordered rule
    /// (objects file first, then the <c>MachineOfWar</c> trait), so a loot object carrying that trait
    /// would silently classify as <c>object</c>; fail the build instead so the conflict is visible.
    /// </summary>
    internal static void ValidateNpcs(
        IReadOnlyDictionary<string, GameCatalogFactionNpcs> npcsByFaction,
        List<GameCatalogValidationError> errors)
    {
        if (!npcsByFaction.TryGetValue(GameCatalogDenormalizer.NpcObjectsSourceKey, out var objects))
        {
            return;
        }

        foreach (var npc in objects.Npcs)
        {
            if (npc.Traits.Contains(GameCatalogDenormalizer.MachineOfWarTraitId, StringComparer.Ordinal))
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.Npcs,
                    "ConflictingNpcKind",
                    $"NPC '{npc.Id}' in '{GameCatalogDenormalizer.NpcObjectsSourceKey}' carries the '{GameCatalogDenormalizer.MachineOfWarTraitId}' trait; an object cannot also be a Machine of War."));
            }
        }
    }
}
