namespace TacticusPlanner.Api.Features.PlayerData;

/// <summary>
/// The served player-data chunk keys — the per-profile analogue of
/// <c>TacticusPlanner.GameCatalog.Models.GameCatalogDatasets</c>. Each key maps to one jsonb column on
/// <c>PlayerDataSnapshot</c> and one manifest entry / chunk endpoint.
/// </summary>
public static class PlayerDataChunkKeys
{
    /// <summary>Public route prefix the served chunks are mounted under (used to build manifest urls).</summary>
    public const string RoutePrefix = "/api/v1/me/player-data";

    public const string PlayerDetails = "player-details";
    public const string Characters = "characters";
    public const string Mows = "mows";
    public const string InventoryUpgrades = "inventory-upgrades";
    public const string InventoryItems = "inventory-items";
    public const string Inventory = "inventory";
    public const string CampaignProgress = "campaign-progress";
    public const string CampaignEventsProgress = "campaign-events-progress";
    public const string GameModeTokens = "game-mode-tokens";
    public const string LreProgress = "lre-progress";

    /// <summary>The full set of chunk keys the manifest advertises, in served order.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        PlayerDetails,
        Characters,
        Mows,
        InventoryUpgrades,
        InventoryItems,
        Inventory,
        CampaignProgress,
        CampaignEventsProgress,
        GameModeTokens,
        LreProgress,
    ];
}
