namespace TacticusPlanner.GameCatalog.Models;

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
    string AbilityId,
    bool IsRelic,
    bool IsUniqueRelic,
    IReadOnlyList<string> AllowedUnits,
    IReadOnlyList<string> AllowedFactions,
    IReadOnlyList<GameCatalogEquipmentLevel> Levels,
    // The cost of each level, from the same datamine level (level 1's is the craft cost).
    IReadOnlyList<GameCatalogEquipmentUpgradeLevel> UpgradeLevels
);

// ---- datamine shape (bound from Data/datamine/raw/gameconfig/items.json, keyed by item id) -------------

public sealed record DatamineItem(
    string Name,
    string ItemType,
    string Rarity,
    IReadOnlyList<DatamineItemLevel> Levels,
    IReadOnlyList<string>? AllowedFactions,
    IReadOnlyList<string>? AllowedUnits,
    bool? IsRelic,
    bool? IsUniqueRelic,
    string? AbilityId
);

public sealed record DatamineItemLevel(
    IReadOnlyDictionary<string, int> Stats,
    int? GoldCost,
    int? DustCost,
    int? MythicDustCost
);

// Equipment with its upgrade-cost ladder inlined, so the client never joins against a shared cost table.
public sealed record GameCatalogEquipmentView(
    string Id,
    string Name,
    string Rarity,
    string Type,
    string AbilityId,
    bool IsRelic,
    bool IsUniqueRelic,
    IReadOnlyList<string> AllowedUnits,
    IReadOnlyList<string> AllowedFactions,
    IReadOnlyList<GameCatalogEquipmentLevel> Levels,
    IReadOnlyList<GameCatalogEquipmentUpgradeLevel> UpgradeLevels
);
