using System.Text.Json;

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
    IReadOnlyList<JsonElement> Levels
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
    IReadOnlyList<JsonElement> Levels,
    IReadOnlyList<GameCatalogEquipmentUpgradeLevel> UpgradeLevels
);
