using System.Text.Json.Serialization;

namespace TacticusPlanner.GameCatalog.Models;

/// <summary>
/// The single <c>horde-mode-tiers</c> record: a curated S/A/B/C/D tier list of how well characters do in
/// horde modes (waves of enemies, such as Legendary Events and Survival), and the power multiplier each
/// tier applies. A character may override its tier's multiplier with its own. A character not listed is
/// unrated and has multiplier 1.0, applied by the client (the server never expands it). Denormalization
/// only sorts <see cref="Characters"/> by unit id.
/// </summary>
public sealed record GameCatalogHordeModeTiers(
    string Id,
    IReadOnlyList<GameCatalogHordeModeTier> Tiers,
    IReadOnlyList<GameCatalogHordeModeCharacter> Characters
);

/// <summary>One tier (<c>S</c>, <c>A</c>, <c>B</c>, <c>C</c> or <c>D</c>) and its power multiplier, in (0, 5].</summary>
public sealed record GameCatalogHordeModeTier(
    string Tier,
    double Multiplier
);

/// <summary>
/// A character's horde-mode tier, and optionally its own multiplier in (0, 5] that replaces the tier's
/// for this character (fine tuning within a tier), and optionally its <see cref="Synergies"/>: up to six
/// recommended teammates as character ids, best first (served in authoring order). Both are omitted
/// from the served JSON when not set; denormalization serves an empty synergy list as not set.
/// </summary>
public sealed record GameCatalogHordeModeCharacter(
    string UnitId,
    string Tier,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? Multiplier = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? Synergies = null
);
