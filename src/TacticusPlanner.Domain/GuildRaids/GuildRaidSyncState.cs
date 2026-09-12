using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.GuildRaids.Enums;
using TacticusPlanner.Domain.Guilds;

namespace TacticusPlanner.Domain.GuildRaids;

/// <summary>
/// One-row-per-guild pointer to the latest successfully persisted Guild Raid observation.
/// </summary>
public class GuildRaidSyncState : BaseEntity
{
    /// <summary>
    /// Primary key and foreign key to the registered guild whose observation is cached.
    /// </summary>
    public GuildId GuildId { get; set; }

    /// <summary>
    /// Whether the successful observation found an active season or explicitly found none.
    /// </summary>
    public GuildRaidObservationState State { get; set; }

    /// <summary>
    /// UTC instant of the latest successful observation; <see langword="null"/> until the first successful refresh.
    /// </summary>
    public DateTimeOffset? ObservedAt { get; set; }

    /// <summary>
    /// UTC instant of the latest refresh attempt, successful or failed. Gates the per-guild forced-refresh cooldown.
    /// </summary>
    public DateTimeOffset LastAttemptedAt { get; set; }

    /// <summary>
    /// Persisted active-season snapshot for an <see cref="GuildRaidObservationState.Active"/> observation;
    /// <see langword="null"/> for <see cref="GuildRaidObservationState.NoActiveSeason"/>.
    /// </summary>
    public GuildRaidSeasonId? ActiveSeasonId { get; set; }

    /// <summary>
    /// Registered guild navigation for <see cref="GuildId"/>.
    /// </summary>
    public virtual Guild? Guild { get; set; }

    /// <summary>
    /// Active-season navigation for <see cref="ActiveSeasonId"/>; absent when no season was active.
    /// </summary>
    public virtual GuildRaidSeason? ActiveSeason { get; set; }
}
