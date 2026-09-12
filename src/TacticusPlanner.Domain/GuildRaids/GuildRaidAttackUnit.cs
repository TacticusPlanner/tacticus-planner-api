using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.GuildRaids.Enums;

namespace TacticusPlanner.Domain.GuildRaids;

/// <summary>
/// One hero or Machine of War reported as participating in a persisted Guild Raid attack.
/// </summary>
public class GuildRaidAttackUnit : BaseEntity<GuildRaidAttackUnitId>
{
    /// <summary>
    /// Owning attack; the unit row is deleted when that attack is deleted.
    /// </summary>
    public GuildRaidAttackId GuildRaidAttackId { get; set; }

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

    /// <summary>
    /// Attack navigation for <see cref="GuildRaidAttackId"/>.
    /// </summary>
    public virtual GuildRaidAttack? Attack { get; set; }
}
