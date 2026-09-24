using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    public const string NpcKindUnit = "unit";
    public const string NpcKindMachineOfWar = "machineOfWar";
    public const string NpcKindObject = "object";

    /// <summary>The raw source key whose records are loot objects rather than units.</summary>
    public const string NpcObjectsSourceKey = "npcs-objects";

    /// <summary>The trait id that marks an NPC as a Machine of War. Ids spell the token inconsistently
    /// (<c>MoW</c> / <c>Mow</c>), so the trait — not the id — is the classifier.</summary>
    public const string MachineOfWarTraitId = "MachineOfWar";

    /// <summary>
    /// Flattens the per-faction raw files into the served <c>npcs</c> list, ordered by source key (ordinal)
    /// then source order, stamping each record with its owning file's faction/alliance and its
    /// <see cref="ClassifyNpc"/> kind. Stat ladders pass through untouched.
    /// </summary>
    public static IReadOnlyList<GameCatalogNpc> BuildNpcs(IReadOnlyDictionary<string, GameCatalogFactionNpcs> npcsByFaction) =>
        npcsByFaction
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => pair.Value.Npcs.Select(npc => ToServedNpc(pair.Key, pair.Value, npc)))
            .ToArray();

    /// <summary>
    /// <c>object</c> when loaded from <see cref="NpcObjectsSourceKey"/>, else <c>machineOfWar</c> when the
    /// traits contain <see cref="MachineOfWarTraitId"/>, else <c>unit</c>. Evaluated in that order.
    /// </summary>
    public static string ClassifyNpc(string sourceKey, IReadOnlyList<string> traits) =>
        string.Equals(sourceKey, NpcObjectsSourceKey, StringComparison.Ordinal) ? NpcKindObject
        : traits.Contains(MachineOfWarTraitId, StringComparer.Ordinal) ? NpcKindMachineOfWar
        : NpcKindUnit;

    private static GameCatalogNpc ToServedNpc(string sourceKey, GameCatalogFactionNpcs faction, GameCatalogRawNpc npc) =>
        new(
            npc.Id,
            npc.Name,
            faction.FactionId,
            faction.Alliance,
            ClassifyNpc(sourceKey, npc.Traits),
            npc.MeleeDamage,
            npc.MeleeHits,
            npc.RangedDamage,
            npc.RangedHits,
            npc.Distance,
            npc.Movement,
            npc.Traits,
            npc.ActiveAbilityDamage,
            npc.ActiveAbilities,
            npc.PassiveAbilityDamage,
            npc.PassiveAbilities,
            npc.Stats);
}
