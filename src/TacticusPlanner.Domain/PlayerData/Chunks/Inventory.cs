namespace TacticusPlanner.Domain.PlayerData.Chunks;

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

/// <summary>Remaining inventory categories not split into their own chunk. Shard holdings live in
/// their own dedicated <see cref="InventoryShardRecord"/> chunk instead — that was the one inventory
/// sub-collection worth a dedicated chunk; the rest stay bundled here rather than each getting split
/// out too.</summary>
public sealed class InventoryChunk
{
    public List<InventoryXpBookRecord> XpBooks { get; set; } = [];

    public PlayerAbilityBadgesRecord AbilityBadges { get; set; } = new();

    public MowComponentsRecord Components { get; set; } = new();

    public List<PlayerRarityAmountRecord> ForgeBadges { get; set; } = [];

    public PlayerOrbsRecord Orbs { get; set; } = new();

    public int RequisitionOrdersRegular { get; set; }

    public int RequisitionOrdersBlessed { get; set; }

    public int ResetStones { get; set; }
}

/// <summary>Regular + mythic shard progress toward unlocking a unit the roster doesn't have yet,
/// merged into a single row keyed by <see cref="UnitId"/> — the Tacticus API returns these as two
/// separate lists, but there is no per-unit query that only ever wants one of the two, so they're
/// combined here instead of being two separate id-keyed collections. Only covers units absent from
/// <c>Characters</c>/<c>Mows</c>: once a unit is unlocked, its shard counts live on
/// <see cref="PlayerBaseUnitRecord.Shards"/>/<see cref="PlayerBaseUnitRecord.MythicShards"/> instead,
/// so this chunk never duplicates that count for an already-unlocked unit.</summary>
public sealed class InventoryShardRecord
{
    public UnitId UnitId { get; set; } = UnitId.From(string.Empty);

    public long Amount { get; set; }

    public long MythicAmount { get; set; }
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
