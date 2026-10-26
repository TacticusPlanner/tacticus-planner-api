using System.Text.Json.Serialization;

namespace TacticusPlanner.GameCatalog.Models;

public sealed record GameCatalogMow(
    string Id,
    string Name,
    string UnitKind,
    string Faction,
    string Alliance,
    GameCatalogMowAbility PrimaryAbility,
    GameCatalogMowAbility SecondaryAbility,
    // The ability unlocked at Mythic rarity, the datamine lineup.json mythicAbilities[0]. It
    // levels with the MoW's blue stars, not upgrade materials, so it carries no recipes. Omitted (not null)
    // when unknown.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    GameCatalogMowMythicAbility? MythicAbility = null
);

public sealed record GameCatalogMowAbility(
    string Name,
    IReadOnlyList<IReadOnlyList<string>> Recipes
);

public sealed record GameCatalogMowMythicAbility(string Name);

// MoW ability cost rung, read from the datamine units/abilityUpgradeCostsMoW.json (dust → Salvage,
// abilityToken{Rarity} → Badges, itemAscensionResource_{Rarity} → ForgeBadges, machinesOfWarToken → Components).
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

// Character ability cost rung, read from the datamine units/abilityUpgradeCosts.json: Lvl N (the row's
// 1-based position) is the cost to raise an ability from level N to N + 1. Badges is null when the row does
// not name exactly one abilityToken{Rarity} key (validation reports it).
public sealed record GameCatalogCharacterAbilityCost(
    int Lvl,
    int Gold,
    GameCatalogAmountByRarity? Badges
);

// The served projection, keyed by the ability level it raises to (Level = Lvl + 1).
public sealed record GameCatalogCharacterAbilityCostView(
    int Level,
    int Gold,
    GameCatalogAmountByRarity Badges
);

public sealed record GameCatalogAmountByRarity(
    string Rarity,
    int Amount
);
