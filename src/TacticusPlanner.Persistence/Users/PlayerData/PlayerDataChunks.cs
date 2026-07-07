using System.Text.Json.Serialization;

namespace TacticusPlanner.Persistence.Users.PlayerData;

// Normalized, transformed shapes persisted in PlayerDataSnapshot's jsonb chunk columns. These are
// deliberately NOT the raw TacticusApi.Models.Player.* response types — per ADR 0007 the raw
// response is never stored as-is. Each type here is owned (via OwnsOne/OwnsMany + ToJson()) by
// exactly one PlayerDataSnapshot column. See TacticusPlanner.Api's player-data transformation
// for the mapping from TacticusApi.Models.Player.PlayerResponse into these shapes.
//
// Fields that duplicate what the static game catalog already knows for a given id (display name,
// rarity, faction/grand alliance) are intentionally omitted — callers cross-reference the catalog
// by id instead of storing a redundant copy that could drift.

/// <summary>0 = Stone I, 3 = Iron I, 6 = Bronze I, 9 = Silver I, 12 = Gold I, 15 = Diamond I, 18 =
/// Adamantine I — Tacticus's raw per-unit rank int is a direct 0-based index into this same 21-step
/// ladder (confirmed against a real player response), matching the client's rankOrder exactly.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlayerRank
{
    Stone1 = 0,
    Stone2 = 1,
    Stone3 = 2,
    Iron1 = 3,
    Iron2 = 4,
    Iron3 = 5,
    Bronze1 = 6,
    Bronze2 = 7,
    Bronze3 = 8,
    Silver1 = 9,
    Silver2 = 10,
    Silver3 = 11,
    Gold1 = 12,
    Gold2 = 13,
    Gold3 = 14,
    Diamond1 = 15,
    Diamond2 = 16,
    Diamond3 = 17,
    Adamantine1 = 18,
    Adamantine2 = 19,
    Adamantine3 = 20,
}

/// <summary>Star level: 0 = Common, 3 = Uncommon, 6 = Rare, 9 = Epic, 12 = Legendary — Tacticus's raw
/// per-unit progressionIndex int is a direct 0-based index into this same 20-step (rarity, stars)
/// ladder (confirmed: e.g. index 12 lands exactly on "Legendary:RedThreeStars"), matching the client's
/// progressionOrder exactly. Member names can't contain the client's "Rarity:Stars" separator, so each
/// is given its wire string explicitly via <see cref="JsonStringEnumMemberNameAttribute"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlayerProgression
{
    [JsonStringEnumMemberName("Common:None")] CommonNone = 0,
    [JsonStringEnumMemberName("Common:OneStar")] CommonOneStar = 1,
    [JsonStringEnumMemberName("Common:TwoStars")] CommonTwoStars = 2,
    [JsonStringEnumMemberName("Uncommon:TwoStars")] UncommonTwoStars = 3,
    [JsonStringEnumMemberName("Uncommon:ThreeStars")] UncommonThreeStars = 4,
    [JsonStringEnumMemberName("Uncommon:FourStars")] UncommonFourStars = 5,
    [JsonStringEnumMemberName("Rare:FourStars")] RareFourStars = 6,
    [JsonStringEnumMemberName("Rare:FiveStars")] RareFiveStars = 7,
    [JsonStringEnumMemberName("Rare:RedOneStar")] RareRedOneStar = 8,
    [JsonStringEnumMemberName("Epic:RedOneStar")] EpicRedOneStar = 9,
    [JsonStringEnumMemberName("Epic:RedTwoStars")] EpicRedTwoStars = 10,
    [JsonStringEnumMemberName("Epic:RedThreeStars")] EpicRedThreeStars = 11,
    [JsonStringEnumMemberName("Legendary:RedThreeStars")] LegendaryRedThreeStars = 12,
    [JsonStringEnumMemberName("Legendary:RedFourStars")] LegendaryRedFourStars = 13,
    [JsonStringEnumMemberName("Legendary:RedFiveStars")] LegendaryRedFiveStars = 14,
    [JsonStringEnumMemberName("Legendary:OneBlueStar")] LegendaryOneBlueStar = 15,
    [JsonStringEnumMemberName("Mythic:OneBlueStar")] MythicOneBlueStar = 16,
    [JsonStringEnumMemberName("Mythic:TwoBlueStars")] MythicTwoBlueStars = 17,
    [JsonStringEnumMemberName("Mythic:ThreeBlueStars")] MythicThreeBlueStars = 18,
    [JsonStringEnumMemberName("Mythic:MythicWings")] MythicMythicWings = 19,
}

public sealed class PlayerDetailsChunk
{
    // The player's own display name — not catalog-inferable (there is no catalog id for a player).
    public string Name { get; set; } = string.Empty;

    public int PowerLevel { get; set; }
}

/// <summary>Shared fields for a character or a MoW. MoWs have no rank and no equipment slots, so
/// those live only on <see cref="PlayerCharacterRecord"/> — see <see cref="PlayerMowRecord"/>.</summary>
public abstract class PlayerBaseUnitRecord
{
    /// <summary>Catalog/Tacticus unit id (stable across both systems; no realignment needed here).</summary>
    public string UnitId { get; set; } = string.Empty;

    public PlayerProgression ProgressionIndex { get; set; }

    public long Xp { get; set; }

    public int XpLevel { get; set; }

    public long Shards { get; set; }

    public long MythicShards { get; set; }

    /// <summary>
    /// Raw ability id/level pairs. Categorizing an ability as active/passive (character) or
    /// primary/secondary (MoW) requires catalog ability metadata and is left to read-time
    /// projection rather than baked into the stored record.
    /// </summary>
    public List<PlayerUnitAbilityRecord> Abilities { get; set; } = [];

    /// <summary>Applied upgrade slot indices (2x3 matrix positions), as returned by the API.</summary>
    public List<int> AppliedUpgradeSlots { get; set; } = [];
}

public sealed class PlayerCharacterRecord : PlayerBaseUnitRecord
{
    public PlayerRank Rank { get; set; }

    public List<PlayerUnitEquipmentSlotRecord> EquippedItems { get; set; } = [];
}

public sealed class PlayerMowRecord : PlayerBaseUnitRecord;

public sealed class PlayerUnitAbilityRecord
{
    public string AbilityId { get; set; } = string.Empty;

    public int Level { get; set; }
}

public sealed class PlayerUnitEquipmentSlotRecord
{
    public string SlotId { get; set; } = string.Empty;

    public string EquipmentId { get; set; } = string.Empty;

    public int Level { get; set; }
}

public sealed class InventoryUpgradeRecord
{
    public string UpgradeId { get; set; } = string.Empty;

    public long Amount { get; set; }
}

public sealed class InventoryItemRecord
{
    public string ItemId { get; set; } = string.Empty;

    public int Level { get; set; }

    public long Amount { get; set; }
}

/// <summary>Remaining inventory categories not split into their own chunk.</summary>
public sealed class InventoryChunk
{
    public List<InventoryShardRecord> Shards { get; set; } = [];

    public List<InventoryShardRecord> MythicShards { get; set; } = [];

    public List<InventoryXpBookRecord> XpBooks { get; set; } = [];

    public PlayerAbilityBadgesRecord AbilityBadges { get; set; } = new();

    public MowComponentsRecord Components { get; set; } = new();

    public List<PlayerRarityAmountRecord> ForgeBadges { get; set; } = [];

    public PlayerOrbsRecord Orbs { get; set; } = new();

    public int RequisitionOrdersRegular { get; set; }

    public int RequisitionOrdersBlessed { get; set; }

    public int ResetStones { get; set; }
}

public sealed class InventoryShardRecord
{
    public string UnitId { get; set; } = string.Empty;

    public long Amount { get; set; }
}

public sealed class InventoryXpBookRecord
{
    public string XpBookId { get; set; } = string.Empty;

    public long Amount { get; set; }
}

/// <summary>Badge name is resolved from the game catalog by rarity/alliance at read time, so it's
/// not duplicated here — same reasoning as the other catalog-inferable fields dropped elsewhere.</summary>
public sealed class PlayerAbilityBadgesRecord
{
    public List<PlayerRarityAmountRecord> Imperial { get; set; } = [];

    public List<PlayerRarityAmountRecord> Xenos { get; set; } = [];

    public List<PlayerRarityAmountRecord> Chaos { get; set; } = [];
}

/// <summary>MoW components have no per-item identity worth tracking — just the total count per
/// grand alliance, mirroring how Orbs/AbilityBadges are already split.</summary>
public sealed class MowComponentsRecord
{
    public ComponentAmountRecord Imperial { get; set; } = new();

    public ComponentAmountRecord Xenos { get; set; } = new();

    public ComponentAmountRecord Chaos { get; set; } = new();
}

public sealed class ComponentAmountRecord
{
    public long Amount { get; set; }
}

public sealed class PlayerOrbsRecord
{
    public List<PlayerRarityAmountRecord> Imperial { get; set; } = [];

    public List<PlayerRarityAmountRecord> Xenos { get; set; } = [];

    public List<PlayerRarityAmountRecord> Chaos { get; set; } = [];
}

public sealed class PlayerRarityAmountRecord
{
    public string Rarity { get; set; } = string.Empty;

    public long Amount { get; set; }
}

/// <summary>
/// One campaign's progress identity. Used by both the <c>campaign-progress</c> chunk (standard/
/// mirror/elite/elite-mirror) and the <c>campaign-events-progress</c> chunk (limited-time campaign
/// events). <c>TacticusCampaignId</c> is unconditionally also the static catalog's campaign group id
/// — every Tacticus campaign id now has a matching catalog group (see
/// <c>GameCatalogDatasets.CampaignBattleGroups</c>), so no separate cross-reference field is needed.
/// Per-battle attempt data lives in the <c>live-progress</c> chunk instead (it changes far more often
/// than this identity/high-water-mark record does).
/// </summary>
public sealed class CampaignProgressRecord
{
    /// <summary>The Tacticus API's own campaign id — also the catalog's groupId (e.g. <c>campaign1</c>,
    /// <c>mirror1</c>, <c>elite1</c>, <c>eliteMirror1</c>, <c>eventCampaign6</c>).</summary>
    public string TacticusCampaignId { get; set; } = string.Empty;

    /// <summary>Tacticus campaign type: Standard/Mirror/Elite/EliteMirror for storylines, Standard/Extremis
    /// for campaign events.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The highest battle index the player has completed in this campaign.</summary>
    public int HighestCompletedBattleIndex { get; set; }
}

public sealed class GuildRaidTokensRecord
{
    public TokenBucketRecord Tokens { get; set; } = new();

    public TokenBucketRecord BombTokens { get; set; } = new();
}

public sealed class TokenBucketRecord
{
    public int Current { get; set; }

    public int Max { get; set; }

    public int NextTokenInSeconds { get; set; }

    public int RegenDelayInSeconds { get; set; }
}

public sealed class GameModeTokensChunk
{
    public TokenBucketRecord? Arena { get; set; }

    public GuildRaidTokensRecord? GuildRaid { get; set; }

    public TokenBucketRecord? Onslaught { get; set; }

    public TokenBucketRecord? SalvageRun { get; set; }
}

/// <summary>Often-changing data kept in its own chunk (<c>live-progress</c>) so it can be re-synced/
/// re-stored independently of the much-less-volatile roster/inventory/campaign-identity chunks.</summary>
public sealed class LiveProgressChunk
{
    /// <summary>Per-battle attempt counters, derived from campaign progress. Replaces the battle list that
    /// used to live on <see cref="CampaignProgressRecord"/> — this changes daily, that doesn't.</summary>
    public List<BattleAttemptRecord> BattleAttempts { get; set; } = [];

    /// <summary>The <see cref="CampaignProgressRecord.TacticusCampaignId"/> of whichever event-type
    /// campaign is currently present in the synced response, if any.</summary>
    public string? ActiveCampaignEventId { get; set; }

    public GameModeTokensChunk GameModeTokens { get; set; } = new();
}

public sealed class BattleAttemptRecord
{
    /// <summary>Matches <see cref="CampaignProgressRecord.TacticusCampaignId"/>.</summary>
    public string TacticusCampaignId { get; set; } = string.Empty;

    public int BattleIndex { get; set; }

    public int AttemptsLeft { get; set; }

    public int AttemptsUsed { get; set; }
}

/// <summary>One Legendary Release Event's player progress. Static event structure (battle configs,
/// objectives, enemies) already lives in the game catalog's <c>lres</c>/<c>lre-battles</c> datasets and is
/// intentionally not duplicated here. Track shape mirrors <c>GameCatalogLreView</c>'s named
/// Alpha/Beta/Gamma properties (Tacticus's lane ids 1/2/3) rather than a generic indexed list.</summary>
public sealed class LreProgressRecord
{
    /// <summary>The event's unit snowprint id (e.g. <c>"emperLucius"</c>) — matches the catalog's
    /// <c>GameCatalogLreView.Id</c> directly; no id remapping needed for LRE.</summary>
    public string Id { get; set; } = string.Empty;

    public LreTrackProgressRecord? Alpha { get; set; }

    public LreTrackProgressRecord? Beta { get; set; }

    public LreTrackProgressRecord? Gamma { get; set; }

    public int CurrentPoints { get; set; }

    public int CurrentCurrency { get; set; }

    public int CurrentShards { get; set; }

    public int CurrentClaimedChestIndex { get; set; }

    public int? CurrentEventRun { get; set; }

    public TokenBucketRecord? CurrentEventTokens { get; set; }

    public bool? HasUsedAdForExtraTokenToday { get; set; }

    public int? ExtraCurrencyPerPayout { get; set; }
}

public sealed class LreTrackProgressRecord
{
    public List<LreEncounterProgressRecord> Encounters { get; set; } = [];
}

public sealed class LreEncounterProgressRecord
{
    public List<int> ObjectivesCleared { get; set; } = [];

    public int HighScore { get; set; }

    public int EncounterPoints { get; set; }
}
