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

        var units = response.Player?.Units ?? [];
        var characters = units.Where(unit => !mowIds.Contains(unit.Id)).Select(MapCharacter).ToList();
        var mows = units.Where(unit => mowIds.Contains(unit.Id)).Select(MapMow).ToList();

        var inventory = response.Player?.Inventory;
        var inventoryUpgrades = (inventory?.Upgrades ?? []).Select(MapUpgrade).ToList();
        var inventoryItems = (inventory?.Items ?? []).Select(MapItem).ToList();
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

    /// <summary>
    /// The Tacticus API's own campaign ids follow two shapes: the always-available storyline chains
    /// (<c>campaignN</c>/<c>mirrorN</c>/<c>eliteN</c>/<c>eliteMirrorN</c>) and rotating limited-time events
    /// (<c>eventCampaignN</c>). This is the only reliable signal observed in a real response — there is no
    /// separate "is this an event" flag on the campaign progress payload.
    /// </summary>
    private static bool IsEventCampaign(string tacticusCampaignId) =>
        tacticusCampaignId.StartsWith("eventCampaign", StringComparison.Ordinal);

    private static PlayerCharacterRecord MapCharacter(TacticusApiPlayer.Unit unit) => new()
    {
        UnitId = unit.Id,
        ProgressionIndex = (PlayerProgression)unit.ProgressionIndex,
        Xp = unit.Xp,
        XpLevel = unit.XpLevel,
        Rank = (PlayerRank)unit.Rank,
        Shards = unit.Shards,
        MythicShards = unit.MythicShards,
        Abilities = MapAbilities(unit.Abilities),
        AppliedUpgradeSlots = (unit.Upgrades ?? []).ToList(),
        EquippedItems = (unit.Items ?? [])
            .Select(item => new PlayerUnitEquipmentSlotRecord
            {
                SlotId = item.SlotId,
                EquipmentId = item.Id,
                Level = item.Level,
            })
            .ToList(),
    };

    // MoWs have no rank and no equipment slots — see PlayerBaseUnitRecord/PlayerMowRecord.
    private static PlayerMowRecord MapMow(TacticusApiPlayer.Unit unit) => new()
    {
        UnitId = unit.Id,
        ProgressionIndex = (PlayerProgression)unit.ProgressionIndex,
        Xp = unit.Xp,
        XpLevel = unit.XpLevel,
        Shards = unit.Shards,
        MythicShards = unit.MythicShards,
        Abilities = MapAbilities(unit.Abilities),
        AppliedUpgradeSlots = (unit.Upgrades ?? []).ToList(),
    };

    private static List<PlayerUnitAbilityRecord> MapAbilities(IEnumerable<TacticusApiPlayer.Ability>? abilities) =>
        (abilities ?? [])
            .Select(ability => new PlayerUnitAbilityRecord { AbilityId = ability.Id, Level = ability.Level })
            .ToList();

    private static InventoryUpgradeRecord MapUpgrade(TacticusApiPlayer.Upgrade upgrade) => new()
    {
        UpgradeId = upgrade.Id,
        Amount = upgrade.Amount,
    };

    private static InventoryItemRecord MapItem(TacticusApiPlayer.InventoryEquipment item) => new()
    {
        ItemId = item.Id,
        Level = item.Level,
        Amount = item.Amount,
    };

    private static InventoryChunk MapInventory(TacticusApiPlayer.Inventory? inventory) => new()
    {
        Shards = (inventory?.Shards ?? []).Select(MapShard).ToList(),
        MythicShards = (inventory?.MythicShards ?? []).Select(MapShard).ToList(),
        XpBooks = (inventory?.XpBooks ?? [])
            .Select(book => new InventoryXpBookRecord { XpBookId = book.Id, Amount = book.Amount })
            .ToList(),
        AbilityBadges = new PlayerAbilityBadgesRecord
        {
            Imperial = (inventory?.AbilityBadges?.Imperial ?? []).Select(MapNamedRarityAmount).ToList(),
            Xenos = (inventory?.AbilityBadges?.Xenos ?? []).Select(MapNamedRarityAmount).ToList(),
            Chaos = (inventory?.AbilityBadges?.Chaos ?? []).Select(MapNamedRarityAmount).ToList(),
        },
        Components = MapMowComponents(inventory?.Components),
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
        UnitId = shard.Id,
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

    // MoW components have no per-item identity worth tracking — just the total count per grand
    // alliance, mirroring how Orbs/AbilityBadges are already split.
    private static MowComponentsRecord MapMowComponents(IEnumerable<TacticusApiPlayer.MoWComponent>? components)
    {
        var list = (components ?? []).ToList();
        return new MowComponentsRecord
        {
            Imperial = list.Where(component => component.GrandAlliance == "Imperial").Sum(component => component.Amount),
            Xenos = list.Where(component => component.GrandAlliance == "Xenos").Sum(component => component.Amount),
            Chaos = list.Where(component => component.GrandAlliance == "Chaos").Sum(component => component.Amount),
        };
    }

    private static CampaignProgressRecord MapCampaign(TacticusApiPlayer.CampaignProgress campaign) => new()
    {
        TacticusCampaignId = campaign.Id,
        Type = campaign.Type,
        HighestCompletedBattleIndex = HighestBattleIndex(campaign),
    };

    private static IEnumerable<BattleAttemptRecord> MapBattleAttempts(TacticusApiPlayer.CampaignProgress campaign) =>
        (campaign.Battles ?? [])
            .Select(battle => new BattleAttemptRecord
            {
                TacticusCampaignId = campaign.Id,
                BattleIndex = battle.BattleIndex,
                AttemptsLeft = battle.AttemptsLeft,
                AttemptsUsed = battle.AttemptsUsed,
            });

    private static int HighestBattleIndex(TacticusApiPlayer.CampaignProgress campaign)
    {
        var battles = campaign.Battles ?? [];
        return battles.Count == 0 ? -1 : battles.Max(battle => battle.BattleIndex);
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

    private static LreProgressRecord MapLre(TacticusApiPlayer.LegendaryEvent lre)
    {
        var lanesById = (lre.Lanes ?? []).ToDictionary(lane => lane.Id);

        return new LreProgressRecord
        {
            Id = lre.Id,
            Alpha = MapLreTrack(lanesById.GetValueOrDefault(1)),
            Beta = MapLreTrack(lanesById.GetValueOrDefault(2)),
            Gamma = MapLreTrack(lanesById.GetValueOrDefault(3)),
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

    // Tacticus's lane ids 1/2/3 correspond to the catalog's named Alpha/Beta/Gamma tracks
    // (GameCatalogLreView) — mirrored here instead of a generic indexed lane list.
    private static LreTrackProgressRecord? MapLreTrack(TacticusApiPlayer.LegendaryEventLane? lane)
    {
        if (lane is null)
        {
            return null;
        }

        return new LreTrackProgressRecord
        {
            Encounters = (lane.Progress ?? [])
                .Select(progress => new LreEncounterProgressRecord
                {
                    ObjectivesCleared = (progress.ObjectivesCleared ?? []).ToList(),
                    HighScore = progress.HighScore,
                    EncounterPoints = progress.EncounterPoints,
                })
                .ToList(),
        };
    }
}

public sealed record PlayerDataTransformResult(
    PlayerDetailsChunk PlayerDetails,
    List<PlayerCharacterRecord> Characters,
    List<PlayerMowRecord> Mows,
    List<InventoryUpgradeRecord> InventoryUpgrades,
    List<InventoryItemRecord> InventoryItems,
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
