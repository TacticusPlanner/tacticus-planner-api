using TacticusPlanner.Domain.PlayerData.Chunks;

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
    public const string InventoryShards = "inventory-shards";
    public const string Inventory = "inventory";
    public const string CampaignProgress = "campaign-progress";
    public const string CampaignEventsProgress = "campaign-events-progress";
    // Battle attempts, the active campaign-event id, and game-mode tokens — the often-changing data,
    // kept in its own chunk so it can be re-synced independently of the much less volatile chunks above.
    public const string LiveProgress = "live-progress";
    public const string LreProgress = "lre-progress";

    /// <summary>The full set of chunk keys the manifest advertises, in served order.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        PlayerDetails,
        Characters,
        Mows,
        InventoryUpgrades,
        InventoryItems,
        InventoryShards,
        Inventory,
        CampaignProgress,
        CampaignEventsProgress,
        LiveProgress,
        LreProgress,
    ];

    private static readonly Dictionary<string, Func<object>> EmptyPayloadFactories =
        new Dictionary<string, Func<object>>(StringComparer.Ordinal)
        {
            [PlayerDetails] = static () => new PlayerDetailsChunk(),
            [Characters] = static () => Array.Empty<PlayerCharacterRecord>(),
            [Mows] = static () => Array.Empty<PlayerMowRecord>(),
            [InventoryUpgrades] = static () => Array.Empty<InventoryUpgradeRecord>(),
            [InventoryItems] = static () => Array.Empty<InventoryItemRecord>(),
            [InventoryShards] = static () => Array.Empty<InventoryShardRecord>(),
            [Inventory] = static () => new InventoryChunk(),
            [CampaignProgress] = static () => Array.Empty<CampaignProgressRecord>(),
            [CampaignEventsProgress] = static () => Array.Empty<CampaignProgressRecord>(),
            [LiveProgress] = static () => new LiveProgressChunk(),
            [LreProgress] = static () => Array.Empty<LreProgressRecord>(),
        };

    static PlayerDataChunkKeys()
    {
        var missingDefault = All.FirstOrDefault(key => !EmptyPayloadFactories.ContainsKey(key));
        if (missingDefault is not null || EmptyPayloadFactories.Count != All.Count)
        {
            throw new InvalidOperationException(
                $"Every player-data chunk must define exactly one empty payload factory. Missing: '{missingDefault}'.");
        }
    }

    /// <summary>Creates the non-null wire-contract value used when a legacy snapshot has no persisted
    /// payload for a chunk introduced by a newer schema.</summary>
    public static object CreateEmptyPayload(string chunk) =>
        EmptyPayloadFactories.TryGetValue(chunk, out var factory)
            ? factory()
            : throw new ArgumentOutOfRangeException(nameof(chunk), chunk, "Unknown player-data chunk key.");
}
