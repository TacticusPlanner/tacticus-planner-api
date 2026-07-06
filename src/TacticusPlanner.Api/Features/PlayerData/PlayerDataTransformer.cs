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
/// </summary>
public sealed class PlayerDataTransformer(IGameCatalogProvider catalog)
{
    /// <summary>Bumped when the persisted/served chunk shapes change in a way clients must react to.</summary>
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PlayerDataTransformResult Transform(TacticusApiPlayer.PlayerResponse response)
    {
        var snapshot = catalog.Current;
        var mowIds = new HashSet<string>(snapshot.Mows.Select(mow => mow.Id), StringComparer.Ordinal);

        var characters = new List<PlayerUnitRecord>();
        var mows = new List<PlayerUnitRecord>();
        foreach (var unit in response.Player?.Units ?? [])
        {
            var record = MapUnit(unit);
            (mowIds.Contains(record.UnitId) ? mows : characters).Add(record);
        }

        var inventory = response.Player?.Inventory;
        var inventoryUpgrades = (inventory?.Upgrades ?? []).Select(MapUpgrade).ToList();
        var inventoryItems = (inventory?.Items ?? []).Select(MapItem).ToList();
        var inventoryChunk = MapInventory(inventory);

        // GameCatalogSnapshot.CampaignGroups is keyed by dataset key (e.g. "campaign-battles-indomitus"), not
        // by the group's own groupId — build a lookup on the groupId values themselves (the field we renamed
        // to align with the Tacticus API's campaign ids).
        var catalogCampaignGroupIds = new HashSet<string>(
            snapshot.CampaignGroups.Values.Select(group => group.GroupId),
            StringComparer.Ordinal);

        var campaignProgress = new List<CampaignProgressRecord>();
        var campaignEventsProgress = new List<CampaignProgressRecord>();
        foreach (var campaign in response.Player?.Progress?.Campaigns ?? [])
        {
            var record = MapCampaign(campaign, catalogCampaignGroupIds);
            (IsEventCampaign(record.TacticusCampaignId) ? campaignEventsProgress : campaignProgress).Add(record);
        }

        var gameModeTokens = MapGameModeTokens(response.Player?.Progress);
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
            [PlayerDataChunkKeys.Inventory] = GameCatalogHashing.ComputeCanonicalJsonHash(inventoryChunk, JsonOptions),
            [PlayerDataChunkKeys.CampaignProgress] = GameCatalogHashing.ComputeCanonicalJsonHash(campaignProgress, JsonOptions),
            [PlayerDataChunkKeys.CampaignEventsProgress] = GameCatalogHashing.ComputeCanonicalJsonHash(campaignEventsProgress, JsonOptions),
            [PlayerDataChunkKeys.GameModeTokens] = GameCatalogHashing.ComputeCanonicalJsonHash(gameModeTokens, JsonOptions),
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
            Inventory: inventoryChunk,
            CampaignProgress: campaignProgress,
            CampaignEventsProgress: campaignEventsProgress,
            GameModeTokens: gameModeTokens,
            LreProgress: lreProgress,
            ChunkHashes: chunkHashes,
            SourceHash: sourceHash,
            ConfigHash: response.Metadata?.ConfigHash ?? string.Empty,
            TacticusLastUpdatedOn: response.Metadata?.LastUpdatedOn ?? 0);
    }

    /// <summary>
    /// The Tacticus API's own campaign ids follow two shapes: the always-available storyline chains
    /// (<c>campaignN</c>/<c>mirrorN</c>/<c>eliteN</c>/<c>eliteMirrorN</c>) and rotating limited-time events
    /// (<c>eventCampaignN</c>). This is the only reliable signal observed in a real response — there is no
    /// separate "is this an event" flag on the campaign progress payload.
    /// </summary>
    private static bool IsEventCampaign(string tacticusCampaignId) =>
        tacticusCampaignId.StartsWith("eventCampaign", StringComparison.Ordinal);

    private static PlayerUnitRecord MapUnit(TacticusApiPlayer.Unit unit) => new()
    {
        UnitId = unit.Id,
        Name = unit.Name,
        ProgressionIndex = unit.ProgressionIndex,
        Xp = unit.Xp,
        XpLevel = unit.XpLevel,
        Rank = unit.Rank,
        Shards = unit.Shards,
        MythicShards = unit.MythicShards,
        Abilities = (unit.Abilities ?? [])
            .Select(ability => new PlayerUnitAbilityRecord { AbilityId = ability.Id, Level = ability.Level })
            .ToList(),
        AppliedUpgradeSlots = (unit.Upgrades ?? []).ToList(),
        EquippedItems = (unit.Items ?? [])
            .Select(item => new PlayerUnitEquipmentSlotRecord
            {
                SlotId = item.SlotId,
                EquipmentId = item.Id,
                Name = item.Name,
                Rarity = item.Rarity,
                Level = item.Level,
            })
            .ToList(),
        // Faction/grand alliance are not present on the player endpoint's unit records — populated at
        // read time by cross-referencing the catalog, not stored redundantly here.
        Faction = string.Empty,
        GrandAlliance = string.Empty,
    };

    private static InventoryUpgradeRecord MapUpgrade(TacticusApiPlayer.Upgrade upgrade) => new()
    {
        UpgradeId = upgrade.Id,
        Name = upgrade.Name,
        Amount = upgrade.Amount,
    };

    private static InventoryItemRecord MapItem(TacticusApiPlayer.InventoryEquipment item) => new()
    {
        ItemId = item.Id,
        Name = item.Name,
        Level = item.Level,
        Amount = item.Amount,
    };

    private static InventoryChunk MapInventory(TacticusApiPlayer.Inventory? inventory) => new()
    {
        Shards = (inventory?.Shards ?? []).Select(MapShard).ToList(),
        MythicShards = (inventory?.MythicShards ?? []).Select(MapShard).ToList(),
        XpBooks = (inventory?.XpBooks ?? [])
            .Select(book => new InventoryXpBookRecord { XpBookId = book.Id, Rarity = book.Name, Amount = book.Amount })
            .ToList(),
        AbilityBadges = new PlayerAbilityBadgesRecord
        {
            Imperial = (inventory?.AbilityBadges?.Imperial ?? []).Select(MapNamedRarityAmount).ToList(),
            Xenos = (inventory?.AbilityBadges?.Xenos ?? []).Select(MapNamedRarityAmount).ToList(),
            Chaos = (inventory?.AbilityBadges?.Chaos ?? []).Select(MapNamedRarityAmount).ToList(),
        },
        Components = (inventory?.Components ?? [])
            .Select(component => new PlayerMowComponentRecord
            {
                Name = component.Name,
                GrandAlliance = component.GrandAlliance,
                Amount = component.Amount,
            })
            .ToList(),
        ForgeBadges = (inventory?.ForgeBadges ?? []).Select(MapNamedRarityAmount).ToList(),
        Orbs = new PlayerOrbsRecord
        {
            Imperial = (inventory?.Orbs?.Imperial ?? []).Select(MapRarityAmount).ToList(),
            Xenos = (inventory?.Orbs?.Xenos ?? []).Select(MapRarityAmount).ToList(),
            Chaos = (inventory?.Orbs?.Chaos ?? []).Select(MapRarityAmount).ToList(),
        },
        RequisitionOrdersRegular = inventory?.RequisitionOrders?.Regular ?? 0,
        RequisitionOrdersBlessed = inventory?.RequisitionOrders?.Blessed ?? 0,
        ResetStones = inventory?.ResetStones ?? 0,
    };

    private static InventoryShardRecord MapShard(TacticusApiPlayer.Shard shard) => new()
    {
        ShardId = shard.Id,
        Name = shard.Name,
        Amount = shard.Amount,
    };

    private static PlayerNamedRarityAmountRecord MapNamedRarityAmount(TacticusApiPlayer.AbilityBadge badge) => new()
    {
        Name = badge.Name,
        Rarity = badge.Rarity,
        Amount = badge.Amount,
    };

    private static PlayerNamedRarityAmountRecord MapNamedRarityAmount(TacticusApiPlayer.ForgeBadge badge) => new()
    {
        Name = badge.Name,
        Rarity = badge.Rarity,
        Amount = badge.Amount,
    };

    private static PlayerRarityAmountRecord MapRarityAmount(TacticusApiPlayer.Orb orb) => new()
    {
        Rarity = orb.Rarity,
        Amount = orb.Amount,
    };

    private static CampaignProgressRecord MapCampaign(
        TacticusApiPlayer.CampaignProgress campaign,
        HashSet<string> catalogCampaignGroupIds)
    {
        var battles = (campaign.Battles ?? [])
            .Select(battle => new CampaignBattleProgressRecord
            {
                BattleIndex = battle.BattleIndex,
                AttemptsLeft = battle.AttemptsLeft,
                AttemptsUsed = battle.AttemptsUsed,
            })
            .ToList();

        return new CampaignProgressRecord
        {
            TacticusCampaignId = campaign.Id,
            CatalogCampaignGroupId = catalogCampaignGroupIds.Contains(campaign.Id) ? campaign.Id : null,
            Name = campaign.Name,
            Type = campaign.Type,
            Battles = battles,
            HighestObservedBattleIndex = battles.Count == 0 ? -1 : battles.Max(battle => battle.BattleIndex),
        };
    }

    private static GameModeTokensChunk MapGameModeTokens(TacticusApiPlayer.Progress? progress) => new()
    {
        Arena = MapTokenBucket(progress?.Arena?.Tokens),
        GuildRaid = progress?.GuildRaid is null
            ? null
            : new GuildRaidTokensRecord
            {
                Tokens = MapTokenBucket(progress.GuildRaid.Tokens) ?? new TokenBucketRecord(),
                BombTokens = MapTokenBucket(progress.GuildRaid.BombTokens) ?? new TokenBucketRecord(),
            },
        Onslaught = MapTokenBucket(progress?.Onslaught?.Tokens),
        SalvageRun = MapTokenBucket(progress?.SalvageRun?.Tokens),
    };

    private static TokenBucketRecord? MapTokenBucket(TacticusApiPlayer.TokenInfo? tokens) => tokens is null
        ? null
        : new TokenBucketRecord
        {
            Current = tokens.Current,
            Max = tokens.Max,
            NextTokenInSeconds = tokens.NextTokenInSeconds,
            RegenDelayInSeconds = tokens.RegenDelayInSeconds,
        };

    private static LreProgressRecord MapLre(TacticusApiPlayer.LegendaryEvent lre) => new()
    {
        EventId = lre.Id,
        Lanes = (lre.Lanes ?? [])
            .Select(lane => new LreLaneProgressRecord
            {
                LaneId = lane.Id,
                Name = lane.Name,
                Encounters = (lane.Progress ?? [])
                    .Select(progress => new LreEncounterProgressRecord
                    {
                        ObjectivesCleared = (progress.ObjectivesCleared ?? []).ToList(),
                        HighScore = progress.HighScore,
                        EncounterPoints = progress.EncounterPoints,
                    })
                    .ToList(),
            })
            .ToList(),
        CurrentPoints = lre.CurrentPoints,
        CurrentCurrency = lre.CurrentCurrency,
        CurrentShards = lre.CurrentShards,
        CurrentClaimedChestIndex = lre.CurrentClaimedChestIndex,
        CurrentEventRun = lre.CurrentEvent?.Run,
        CurrentEventTokens = lre.CurrentEvent?.Tokens is { } tokens
            ? new TokenBucketRecord
            {
                Current = tokens.CurrentTokens,
                Max = tokens.MaxTokens,
                NextTokenInSeconds = tokens.NextTokenInSeconds,
                RegenDelayInSeconds = tokens.RegenDelayInSeconds,
            }
            : null,
        HasUsedAdForExtraTokenToday = lre.CurrentEvent?.HasUsedAdForExtraTokenToday,
        ExtraCurrencyPerPayout = lre.CurrentEvent?.ExtraCurrencyPerPayout,
    };
}

public sealed record PlayerDataTransformResult(
    PlayerDetailsChunk PlayerDetails,
    List<PlayerUnitRecord> Characters,
    List<PlayerUnitRecord> Mows,
    List<InventoryUpgradeRecord> InventoryUpgrades,
    List<InventoryItemRecord> InventoryItems,
    InventoryChunk Inventory,
    List<CampaignProgressRecord> CampaignProgress,
    List<CampaignProgressRecord> CampaignEventsProgress,
    GameModeTokensChunk GameModeTokens,
    List<LreProgressRecord> LreProgress,
    Dictionary<string, string> ChunkHashes,
    string SourceHash,
    string ConfigHash,
    long TacticusLastUpdatedOn
);
