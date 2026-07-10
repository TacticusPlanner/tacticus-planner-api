using System.Text.Json;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Utils;
using TacticusPlanner.Persistence.Users.PlayerData;
using TacticusApiPlayer = TacticusPlanner.TacticusApi.Models.Player;

namespace TacticusPlanner.Api.Features.PlayerData;

/// <summary>
/// Transforms a raw Tacticus player endpoint response into the normalized chunk shapes persisted on
/// <see cref="PlayerDataSnapshot"/> — the raw response is never stored as-is (ADR 0007). Also computes the
/// per-chunk canonical-content hashes and the aggregate source hash, reusing
/// <see cref="GameCatalogHashing"/> so the hashing scheme matches the static catalog's.
///
/// Split into a partial class by domain — this file holds only <see cref="Transform"/> and the
/// hashing/result assembly; the per-field mapping helpers live in
/// <c>PlayerDataTransformer.Units.cs</c> (characters/MoWs), <c>PlayerDataTransformer.Inventory.cs</c>
/// (upgrades/items/shards/the remaining inventory chunk), and
/// <c>PlayerDataTransformer.Progress.cs</c> (campaigns/live progress/LRE).
/// </summary>
public sealed partial class PlayerDataTransformer(IGameCatalogProvider catalog)
{
    /// <summary>Bumped when the persisted/served chunk shapes change in a way clients must react to.</summary>
    public const int CurrentSchemaVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PlayerDataTransformResult Transform(TacticusApiPlayer.PlayerResponse response)
    {
        var snapshot = catalog.Current;
        var mowIds = new HashSet<string>(snapshot.Mows.Select(mow => mow.Id), StringComparer.Ordinal);

        var units = response.Player?.Units ?? [];
        var characters = units.Where(unit => !mowIds.Contains(unit.Id)).Select(MapCharacter).ToList();
        var mows = units.Where(unit => mowIds.Contains(unit.Id)).Select(MapMow).ToList();
        var unlockedUnitIds = new HashSet<string>(units.Select(unit => unit.Id), StringComparer.Ordinal);

        var inventory = response.Player?.Inventory;
        var inventoryUpgrades = (inventory?.Upgrades ?? []).Select(MapUpgrade).ToList();
        var inventoryItems = (inventory?.Items ?? []).Select(MapItem).ToList();
        var inventoryShards = MapShards(inventory, unlockedUnitIds);
        var inventoryChunk = MapInventory(inventory);

        // Every Tacticus campaign id is unconditionally also the catalog's groupId now (see
        // GameCatalogDatasets.CampaignBattleGroups) — no lookup/cross-reference needed here.
        var campaigns = response.Player?.Progress?.Campaigns ?? [];
        var campaignProgress = campaigns.Where(c => !IsEventCampaign(c.Id)).Select(MapCampaign).ToList();
        var campaignEventsProgress = campaigns.Where(c => IsEventCampaign(c.Id)).Select(MapCampaign).ToList();

        var liveProgress = new LiveProgressChunk
        {
            BattleAttempts = campaigns.SelectMany(MapBattleAttempts).ToList(),
            ActiveCampaignEventId = campaigns.FirstOrDefault(c => IsEventCampaign(c.Id))?.Id,
            GameModeTokens = MapGameModeTokens(response.Player?.Progress),
        };

        var lreProgress = (response.Player?.Progress?.LegendaryEvents ?? []).Select(MapLre).ToList();

        var playerDetails = new PlayerDetailsChunk
        {
            Name = response.Player?.Details?.Name ?? string.Empty,
            PowerLevel = response.Player?.Details?.PowerLevel ?? 0,
        };

        var chunkHashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PlayerDataChunkKeys.PlayerDetails] = GameCatalogHashing.ComputeCanonicalJsonHash(playerDetails, JsonOptions),
            [PlayerDataChunkKeys.Characters] = GameCatalogHashing.ComputeCanonicalJsonHash(characters, JsonOptions),
            [PlayerDataChunkKeys.Mows] = GameCatalogHashing.ComputeCanonicalJsonHash(mows, JsonOptions),
            [PlayerDataChunkKeys.InventoryUpgrades] = GameCatalogHashing.ComputeCanonicalJsonHash(inventoryUpgrades, JsonOptions),
            [PlayerDataChunkKeys.InventoryItems] = GameCatalogHashing.ComputeCanonicalJsonHash(inventoryItems, JsonOptions),
            [PlayerDataChunkKeys.InventoryShards] = GameCatalogHashing.ComputeCanonicalJsonHash(inventoryShards, JsonOptions),
            [PlayerDataChunkKeys.Inventory] = GameCatalogHashing.ComputeCanonicalJsonHash(inventoryChunk, JsonOptions),
            [PlayerDataChunkKeys.CampaignProgress] = GameCatalogHashing.ComputeCanonicalJsonHash(campaignProgress, JsonOptions),
            [PlayerDataChunkKeys.CampaignEventsProgress] = GameCatalogHashing.ComputeCanonicalJsonHash(campaignEventsProgress, JsonOptions),
            [PlayerDataChunkKeys.LiveProgress] = GameCatalogHashing.ComputeCanonicalJsonHash(liveProgress, JsonOptions),
            [PlayerDataChunkKeys.LreProgress] = GameCatalogHashing.ComputeCanonicalJsonHash(lreProgress, JsonOptions),
        };

        // Reuses the catalog's aggregate-hash scheme (a "version"/"gameVersion" pair is expected by that
        // helper; player data has neither, so both are fixed to the schema version tag). The chunk hashes
        // themselves — the only thing clients actually diff against — are computed the same way as the
        // catalog's per-dataset hashes above.
        var sourceHash = GameCatalogHashing.ComputeSnapshotHash(
            version: response.Metadata?.ConfigHash ?? string.Empty,
            schemaVersion: CurrentSchemaVersion,
            gameVersion: string.Empty,
            datasetHashes: chunkHashes);

        return new PlayerDataTransformResult(
            PlayerDetails: playerDetails,
            Characters: characters,
            Mows: mows,
            InventoryUpgrades: inventoryUpgrades,
            InventoryItems: inventoryItems,
            InventoryShards: inventoryShards,
            Inventory: inventoryChunk,
            CampaignProgress: campaignProgress,
            CampaignEventsProgress: campaignEventsProgress,
            LiveProgress: liveProgress,
            LreProgress: lreProgress,
            ChunkHashes: chunkHashes,
            SourceHash: sourceHash,
            ConfigHash: response.Metadata?.ConfigHash ?? string.Empty,
            TacticusLastUpdatedOn: response.Metadata?.LastUpdatedOn ?? 0);
    }
}

public sealed record PlayerDataTransformResult(
    PlayerDetailsChunk PlayerDetails,
    List<PlayerCharacterRecord> Characters,
    List<PlayerMowRecord> Mows,
    List<InventoryUpgradeRecord> InventoryUpgrades,
    List<InventoryItemRecord> InventoryItems,
    List<InventoryShardRecord> InventoryShards,
    InventoryChunk Inventory,
    List<CampaignProgressRecord> CampaignProgress,
    List<CampaignProgressRecord> CampaignEventsProgress,
    LiveProgressChunk LiveProgress,
    List<LreProgressRecord> LreProgress,
    Dictionary<string, string> ChunkHashes,
    string SourceHash,
    string ConfigHash,
    long TacticusLastUpdatedOn
);
