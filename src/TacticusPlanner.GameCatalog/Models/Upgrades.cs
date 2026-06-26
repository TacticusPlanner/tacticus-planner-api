namespace TacticusPlanner.GameCatalog.Models;

public sealed record GameCatalogUpgrade(
    string Id,
    string Material,
    string SnowprintId,
    string Label,
    string Rarity,
    string Stat,
    string? Icon,
    bool Craftable,
    IReadOnlyList<GameCatalogUpgradeRecipeIngredient> Recipe
);

public sealed record GameCatalogUpgradeRecipeIngredient(
    string Material,
    int Count,
    // Populated server-side for craftable ingredients: the ingredient's own recipe, nested recursively.
    // Null for base (non-craftable) materials. Absent in the raw source JSON (which is flat).
    IReadOnlyList<GameCatalogUpgradeRecipeIngredient>? Recipe = null
);

public sealed record GameCatalogUpgradeView(
    string Id,
    string Material,
    string SnowprintId,
    string Label,
    string Rarity,
    string Stat,
    string? Icon,
    bool Craftable,
    // For craftable upgrades each ingredient carries its own nested recipe (recursively), so the client
    // can walk the full crafting tree without a separate expansion table.
    IReadOnlyList<GameCatalogUpgradeRecipeIngredient> Recipe,
    IReadOnlyList<GameCatalogFarmLocation> FarmLocations
);
