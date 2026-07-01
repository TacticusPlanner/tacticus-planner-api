namespace TacticusPlanner.GameCatalog.Models;

public sealed record GameCatalogMow(
    string Id,
    string Name,
    string UnitKind,
    string Faction,
    string Alliance,
    GameCatalogMowAbility PrimaryAbility,
    GameCatalogMowAbility SecondaryAbility
);

public sealed record GameCatalogMowAbility(
    string Name,
    IReadOnlyList<IReadOnlyList<string>> Recipes
);

public sealed record GameCatalogMowUpgradeCost(
    int Gold,
    int Salvage,
    GameCatalogAmountByRarity Badges,
    GameCatalogAmountByRarity? ForgeBadges,
    int Components
);

// The served projection of a mow upgrade-cost rung, keyed by the ability level it raises a MoW to. The
// raw ladder is a flat array (cost[0] = level 2 … cost[n] = level n+2), so Level correlates the rung with
// the in-game ability level rather than an opaque array index.
public sealed record GameCatalogMowUpgradeCostView(
    int Level,
    int Gold,
    int Salvage,
    GameCatalogAmountByRarity Badges,
    GameCatalogAmountByRarity? ForgeBadges,
    int Components
);

public sealed record GameCatalogAmountByRarity(
    string Rarity,
    int Amount
);
