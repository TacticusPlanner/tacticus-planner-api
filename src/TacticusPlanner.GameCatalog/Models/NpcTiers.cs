using System.Text.Json.Serialization;

namespace TacticusPlanner.GameCatalog.Models;

/// <summary>
/// The single <c>npc-tiers</c> record: a curated S/A/B/C/D tier list of how dangerous enemy NPCs are in
/// horde modes (Legendary Events first), and the power multiplier each tier applies to an NPC's power when
/// the client estimates how deep a team clears. An NPC may override its tier's multiplier with its own. An
/// NPC not listed is unrated and has multiplier 1.0, applied by the client (the server never expands it).
/// Denormalization only sorts <see cref="Npcs"/> by NPC id.
/// </summary>
public sealed record GameCatalogNpcTiers(
    string Id,
    IReadOnlyList<GameCatalogHordeModeTier> Tiers,
    IReadOnlyList<GameCatalogNpcTier> Npcs
);

/// <summary>
/// An NPC's tier, and optionally its own multiplier in (0, 5] that replaces the tier's for this NPC (omitted
/// from the served JSON when not set).
/// </summary>
public sealed record GameCatalogNpcTier(
    string NpcId,
    string Tier,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? Multiplier = null
);
