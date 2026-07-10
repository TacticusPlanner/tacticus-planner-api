using TacticusPlanner.Persistence.Model;

namespace TacticusPlanner.Persistence.Users.PlayerData;

/// <summary>
/// API-synced player data, transformed and normalized from the Tacticus player endpoint response
/// (never the raw response — see ADR 0007 in the docs repo). One row per profile. Each chunk
/// property is mapped to its own jsonb column via EF Core owned-entity JSON mapping
/// (OwnsOne/OwnsMany + ToJson()), split for client-side partial-update granularity.
/// Manual/override data lives separately in <see cref="PlayerDataOverride"/> and is never written here.
/// </summary>
public class PlayerDataSnapshot : BaseEntity<ProfileId>, IRevisionedEntity
{
    /// <summary>The Tacticus player endpoint's <c>metaData.configHash</c>. Compared against the incoming
    /// value on every sync attempt; an unchanged hash skips persistence entirely.</summary>
    public string ConfigHash { get; set; } = string.Empty;

    /// <summary>Unix seconds from the Tacticus player endpoint's <c>metaData.lastUpdatedOn</c> — when the
    /// external API itself last refreshed this player's cached data (not when we last synced it).</summary>
    public long TacticusLastUpdatedOn { get; set; }

    /// <summary>Aggregate canonical-content hash over all chunks, mirroring
    /// <c>GameCatalogHashing.ComputeSnapshotHash</c>. Served as the player-data manifest's top-level hash
    /// and strong ETag.</summary>
    public string SourceHash { get; set; } = string.Empty;

    /// <summary>Version of our own transformed chunk contract (bumped when a chunk's persisted/served
    /// shape changes) — not a game-data version. The Tacticus game-data revision this player data was
    /// computed against is <see cref="ConfigHash"/> itself; there is no separate "game version" signal
    /// from the player endpoint the way the static catalog has one.</summary>
    public int SchemaVersion { get; set; }

    public DateTimeOffset SyncedAt { get; set; }

    public long Revision { get; set; }

    /// <summary>Per-chunk canonical content hash, keyed by chunk key (see player-data chunk keys). Backs
    /// the manifest's <c>chunks[].hash</c> entries. Stored as its own jsonb column (a flat string
    /// dictionary, not an owned object graph).</summary>
    public Dictionary<string, string> ChunkHashes { get; set; } = [];

    public PlayerDetailsChunk PlayerDetails { get; set; } = new();

    public List<PlayerCharacterRecord> Characters { get; set; } = [];

    public List<PlayerMowRecord> Mows { get; set; } = [];

    public List<InventoryUpgradeRecord> InventoryUpgrades { get; set; } = [];

    public List<InventoryItemRecord> InventoryItems { get; set; } = [];

    public List<InventoryShardRecord> InventoryShards { get; set; } = [];

    public InventoryChunk Inventory { get; set; } = new();

    /// <summary>Standard/Mirror/Elite/EliteMirror campaign progress.</summary>
    public List<CampaignProgressRecord> CampaignProgress { get; set; } = [];

    /// <summary>Limited-time campaign-event progress, kept separate from the always-available campaigns
    /// above (they rotate and are frequently absent from a given player's data).</summary>
    public List<CampaignProgressRecord> CampaignEventsProgress { get; set; } = [];

    /// <summary>Often-changing data (battle attempts, active event id, game-mode tokens) — see
    /// <see cref="LiveProgressChunk"/>.</summary>
    public LiveProgressChunk LiveProgress { get; set; } = new();

    public List<LreProgressRecord> LreProgress { get; set; } = [];

    public virtual Profile? Profile { get; set; }
}
