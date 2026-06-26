namespace TacticusPlanner.GameCatalog.Models;

public sealed record GameCatalogEquipmentUpgradeCost(
    string Rarity,
    IReadOnlyList<GameCatalogEquipmentUpgradeLevel> Levels
);

public sealed record GameCatalogEquipmentUpgradeLevel(
    int GoldCost,
    int SalvageCost,
    int MythicSalvageCost
);

// A single equipment level: the per-level stat block, keyed by stat name (e.g. armor/hp,
// blockChance/blockDamage, critChance/critDamage). The set of stat keys varies by equipment type, so the
// block is a string→int map rather than a fixed record.
public sealed record GameCatalogEquipmentLevel(
    IReadOnlyDictionary<string, int> Stats
);

public sealed record GameCatalogEquipment(
    string Id,
    string Name,
    string Rarity,
    string Type,
    string? AbilityId,
    bool IsRelic,
    bool IsUniqueRelic,
    IReadOnlyList<string> AllowedUnits,
    IReadOnlyList<string> AllowedFactions,
    IReadOnlyList<GameCatalogEquipmentLevel> Levels
);

// Equipment with its per-rarity upgrade-cost ladder inlined (the matched rarity's levels), so the client
// never joins against a shared cost table.
public sealed record GameCatalogEquipmentView(
    string Id,
    string Name,
    string Rarity,
    string Type,
    string? AbilityId,
    bool IsRelic,
    bool IsUniqueRelic,
    IReadOnlyList<string> AllowedUnits,
    IReadOnlyList<string> AllowedFactions,
    IReadOnlyList<GameCatalogEquipmentLevel> Levels,
    IReadOnlyList<GameCatalogEquipmentUpgradeLevel> UpgradeLevels
);
