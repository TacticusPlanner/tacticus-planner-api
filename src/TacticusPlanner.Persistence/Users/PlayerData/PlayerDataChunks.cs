namespace TacticusPlanner.Persistence.Users.PlayerData;

// Normalized, transformed shapes persisted in PlayerDataSnapshot's jsonb chunk columns. These are
// deliberately NOT the raw TacticusApi.Models.Player.* response types — per ADR 0007 the raw
// response is never stored as-is. Each type here is owned (via OwnsOne/OwnsMany + ToJson()) by
// exactly one PlayerDataSnapshot column. See TacticusPlanner.Api's player-data transformation
// (Phase 1c) for the mapping from TacticusApi.Models.Player.PlayerResponse into these shapes.

public sealed class PlayerDetailsChunk
{
    public string Name { get; set; } = string.Empty;

    public int PowerLevel { get; set; }
}

/// <summary>
/// One owned unit (character or MoW). Populates both the <c>characters</c> and <c>mows</c> chunks —
/// which chunk a given record belongs to is decided during transformation by cross-referencing the
/// unit id against the game catalog, not by any field on this record.
/// </summary>
public sealed class PlayerUnitRecord
{
    /// <summary>Catalog/Tacticus unit id (stable across both systems; no realignment needed here).</summary>
    public string UnitId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Faction { get; set; } = string.Empty;

    public string GrandAlliance { get; set; } = string.Empty;

    /// <summary>Star level: 0 = Common, 3 = Uncommon, 6 = Rare, 9 = Epic, 12 = Legendary.</summary>
    public int ProgressionIndex { get; set; }

    public long Xp { get; set; }

    public int XpLevel { get; set; }

    /// <summary>0 = Stone I, 3 = Iron I, 6 = Bronze I, 9 = Silver I, 12 = Gold I, 15 = Diamond I.</summary>
    public int Rank { get; set; }

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

    public List<PlayerUnitEquipmentSlotRecord> EquippedItems { get; set; } = [];
}

public sealed class PlayerUnitAbilityRecord
{
    public string AbilityId { get; set; } = string.Empty;

    public int Level { get; set; }
}

public sealed class PlayerUnitEquipmentSlotRecord
{
    public string SlotId { get; set; } = string.Empty;

    public string EquipmentId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Rarity { get; set; } = string.Empty;

    public int Level { get; set; }
}

public sealed class InventoryUpgradeRecord
{
    public string UpgradeId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public long Amount { get; set; }
}

public sealed class InventoryItemRecord
{
    public string ItemId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

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

    public List<PlayerMowComponentRecord> Components { get; set; } = [];

    public List<PlayerNamedRarityAmountRecord> ForgeBadges { get; set; } = [];

    public PlayerOrbsRecord Orbs { get; set; } = new();

    public int RequisitionOrdersRegular { get; set; }

    public int RequisitionOrdersBlessed { get; set; }

    public int ResetStones { get; set; }
}

public sealed class InventoryShardRecord
{
    public string ShardId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public long Amount { get; set; }
}

public sealed class InventoryXpBookRecord
{
    public string XpBookId { get; set; } = string.Empty;

    public string Rarity { get; set; } = string.Empty;

    public long Amount { get; set; }
}

public sealed class PlayerAbilityBadgesRecord
{
    public List<PlayerNamedRarityAmountRecord> Imperial { get; set; } = [];

    public List<PlayerNamedRarityAmountRecord> Xenos { get; set; } = [];

    public List<PlayerNamedRarityAmountRecord> Chaos { get; set; } = [];
}

public sealed class PlayerNamedRarityAmountRecord
{
    public string Name { get; set; } = string.Empty;

    public string Rarity { get; set; } = string.Empty;

    public long Amount { get; set; }
}

public sealed class PlayerMowComponentRecord
{
    public string Name { get; set; } = string.Empty;

    public string GrandAlliance { get; set; } = string.Empty;

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
/// One campaign's progress. Used by both the <c>campaign-progress</c> chunk (standard/mirror/elite/
/// elite-mirror) and the <c>campaign-events-progress</c> chunk (limited-time campaign events).
/// </summary>
public sealed class CampaignProgressRecord
{
    /// <summary>The Tacticus API's own campaign id (e.g. <c>campaign1</c>, <c>mirror1</c>, <c>elite1</c>,
    /// <c>eliteMirror1</c>, <c>eventCampaign6</c>).</summary>
    public string TacticusCampaignId { get; set; } = string.Empty;

    /// <summary>
    /// The matching static catalog campaign group id, when one exists. Null for tiers the catalog does not
    /// yet model (elite/eliteMirror) or events not yet cross-referenced — see the remarks on
    /// <c>GameCatalogDatasets.CampaignBattleGroups</c>. Progress is still persisted even when null.
    /// </summary>
    public string? CatalogCampaignGroupId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Tacticus campaign type, e.g. Standard/Mirror/Elite/EliteMirror/Extremis.</summary>
    public string Type { get; set; } = string.Empty;

    public List<CampaignBattleProgressRecord> Battles { get; set; } = [];

    /// <summary>
    /// The highest <c>battleIndex</c> present in the response for this campaign. This reflects which
    /// battles the API currently exposes attempts for, not a confirmed "highest completed node" — the
    /// exact unlock/completion semantics of the Tacticus battle list have not been empirically confirmed
    /// (see ADR 0007 consequences). Treat as a best-effort progress signal, not ground truth.
    /// </summary>
    public int HighestObservedBattleIndex { get; set; }
}

public sealed class CampaignBattleProgressRecord
{
    public int BattleIndex { get; set; }

    public int AttemptsLeft { get; set; }

    public int AttemptsUsed { get; set; }
}

public sealed class GameModeTokensChunk
{
    public TokenBucketRecord? Arena { get; set; }

    public GuildRaidTokensRecord? GuildRaid { get; set; }

    public TokenBucketRecord? Onslaught { get; set; }

    public TokenBucketRecord? SalvageRun { get; set; }
}

public sealed class TokenBucketRecord
{
    public int Current { get; set; }

    public int Max { get; set; }

    public int NextTokenInSeconds { get; set; }

    public int RegenDelayInSeconds { get; set; }
}

public sealed class GuildRaidTokensRecord
{
    public TokenBucketRecord Tokens { get; set; } = new();

    public TokenBucketRecord BombTokens { get; set; } = new();
}

/// <summary>One Legendary Release Event's player progress. Static event structure (battle configs,
/// objectives, enemies) already lives in the game catalog's <c>lres</c>/<c>lre-battles</c> datasets and is
/// intentionally not duplicated here.</summary>
public sealed class LreProgressRecord
{
    public string EventId { get; set; } = string.Empty;

    public List<LreLaneProgressRecord> Lanes { get; set; } = [];

    public int CurrentPoints { get; set; }

    public int CurrentCurrency { get; set; }

    public int CurrentShards { get; set; }

    public int CurrentClaimedChestIndex { get; set; }

    public int? CurrentEventRun { get; set; }

    public TokenBucketRecord? CurrentEventTokens { get; set; }

    public bool? HasUsedAdForExtraTokenToday { get; set; }

    public int? ExtraCurrencyPerPayout { get; set; }
}

public sealed class LreLaneProgressRecord
{
    public int LaneId { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<LreEncounterProgressRecord> Encounters { get; set; } = [];
}

public sealed class LreEncounterProgressRecord
{
    public List<int> ObjectivesCleared { get; set; } = [];

    public int HighScore { get; set; }

    public int EncounterPoints { get; set; }
}
