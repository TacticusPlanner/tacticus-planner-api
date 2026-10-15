namespace TacticusPlanner.GameCatalog.Models;

/// <summary>
/// The single <c>horde-mode-tiers</c> record: a curated S/A/B/C tier list of how well characters do in
/// horde modes (waves of enemies, such as Legendary Events and Survival), and the power multiplier each
/// tier applies. A character not listed is unrated and has multiplier 1.0, applied by the client (the
/// server never expands it). Denormalization only sorts <see cref="Characters"/> by unit id.
/// </summary>
public sealed record GameCatalogHordeModeTiers(
    string Id,
    IReadOnlyList<GameCatalogHordeModeTier> Tiers,
    IReadOnlyList<GameCatalogHordeModeCharacter> Characters
);

/// <summary>One tier (<c>S</c>, <c>A</c>, <c>B</c> or <c>C</c>) and its power multiplier, in (0, 5].</summary>
public sealed record GameCatalogHordeModeTier(
    string Tier,
    double Multiplier
);

/// <summary>A character's horde-mode tier.</summary>
public sealed record GameCatalogHordeModeCharacter(
    string UnitId,
    string Tier
);
