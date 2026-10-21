using TacticusPlanner.Domain.GuildRaids.Enums;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Domain.GuildRaids;

/// <summary>
/// Normalized, idempotently persisted upstream attack against one Guild Raid encounter. Immutable once
/// written, so it carries no audit timestamps: <see cref="StartedAt"/>/<see cref="CompletedAt"/> are the
/// domain facts.
/// </summary>
public class GuildRaidAttack
{
    public GuildRaidAttackId Id { get; set; }

    /// <summary>
    /// Owning persisted season; attacks are deleted when that season is deleted.
    /// </summary>
    public GuildRaidSeasonId GuildRaidSeasonId { get; set; }

    /// <summary>
    /// SHA-256 content hash of the normalized upstream entry, encoded as 64 lowercase hexadecimal characters.
    /// It is unique within <see cref="GuildRaidSeasonId"/> so refreshes cannot duplicate an attack.
    /// </summary>
    public required string ContentHash { get; set; }

    /// <summary>
    /// 32-byte keyed hash of the attacking player's Tacticus user ID. The plaintext user ID is not persisted.
    /// </summary>
    public required TacticusUserIdHash TacticusUserIdHash { get; set; }

    /// <summary>
    /// Upstream raid tier coordinate used to match the catalog position; typically zero-based.
    /// </summary>
    public int Tier { get; set; }

    /// <summary>
    /// Upstream set coordinate within <see cref="Tier"/>; typically zero-based.
    /// </summary>
    public int Set { get; set; }

    /// <summary>
    /// Zero-based encounter position within the selected raid set; <c>0</c> is a common main-boss position.
    /// </summary>
    public int EncounterIndex { get; set; }

    /// <summary>
    /// Non-negative encounter HP reported after this attack; <c>0</c> means the encounter was defeated.
    /// </summary>
    public int RemainingHp { get; set; }

    /// <summary>
    /// Maximum encounter HP reported with this attack, for example <c>1000000</c>.
    /// </summary>
    public int MaximumHp { get; set; }

    /// <summary>
    /// Whether this attack targeted the main boss or a supporting side boss.
    /// </summary>
    public GuildRaidEncounterType EncounterType { get; set; }

    /// <summary>
    /// Catalog unit-set key after removing the upstream progression suffix, for example
    /// <c>guildBossAvatarMortarion</c> rather than <c>guildBossAvatarMortarion:3</c>.
    /// </summary>
    public required string UnitSetId { get; set; }

    /// <summary>
    /// One-based catalog progression for the exact season tier, set, and encounter. An explicit upstream
    /// suffix is validated against this value; when the source omits its suffix, the catalog supplies it.
    /// </summary>
    public int ProgressionIndex { get; set; }

    /// <summary>
    /// Rarity-based difficulty reported by the upstream attack, such as <see cref="GuildRaidDifficulty.Legendary"/>.
    /// </summary>
    public GuildRaidDifficulty Difficulty { get; set; }

    /// <summary>
    /// Non-negative damage attributed to this entry by the upstream API.
    /// </summary>
    public int DamageDealt { get; set; }

    /// <summary>
    /// Whether the damage came from a normal battle or a raid bomb.
    /// </summary>
    public GuildRaidDamageType DamageType { get; set; }

    /// <summary>
    /// UTC start instant converted from the upstream Unix timestamp; absent when the source omits it.
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// UTC completion instant used as the canonical ordering of attacks.
    /// </summary>
    public DateTimeOffset CompletedAt { get; set; }

    /// <summary>
    /// Season navigation for <see cref="GuildRaidSeasonId"/>.
    /// </summary>
    public virtual GuildRaidSeason? Season { get; set; }

    /// <summary>
    /// Heroes and optional Machine of War reported for this attack, stored inline (jsonb).
    /// </summary>
    public List<GuildRaidAttackUnit> Units { get; set; } = [];
}

/// <summary>
/// One hero or Machine of War reported as participating in a persisted Guild Raid attack.
/// </summary>
public sealed class GuildRaidAttackUnit
{
    /// <summary>
    /// Loosely typed upstream unit key, for example <c>titus</c> or a Machine of War unit ID.
    /// </summary>
    public required string UnitId { get; set; }

    /// <summary>
    /// Identifies whether <see cref="UnitId"/> came from hero details or Machine of War details.
    /// </summary>
    public GuildRaidAttackUnitKind Kind { get; set; }

    /// <summary>
    /// Zero-based order in the upstream hero collection; always <c>0</c> for the single Machine of War slot.
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// Snapshot of the upstream unit power at attack time, for example <c>125000</c>.
    /// </summary>
    public int Power { get; set; }
}
