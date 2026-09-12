using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.Guilds;

namespace TacticusPlanner.Domain.GuildRaids;

/// <summary>
/// Durable normalized snapshot of one upstream Guild Raid season for one registered guild.
/// </summary>
public class GuildRaidSeason : BaseEntity<GuildRaidSeasonId>
{
    /// <summary>
    /// Registered guild that owns this snapshot; combined with <see cref="SeasonNumber"/> it is unique.
    /// </summary>
    public GuildId GuildId { get; set; }

    /// <summary>
    /// Positive upstream season sequence number, for example <c>56</c>.
    /// </summary>
    public int SeasonNumber { get; set; }

    /// <summary>
    /// Loosely typed upstream/catalog key used to resolve the encounter sequence,
    /// for example <c>guild_boss_season_config_5</c>.
    /// </summary>
    public required string SeasonConfigId { get; set; }

    /// <summary>
    /// UTC instant of the most recent successful upstream response that contained this season.
    /// </summary>
    public DateTimeOffset ObservedAt { get; set; }

    /// <summary>
    /// Registered guild navigation for <see cref="GuildId"/>.
    /// </summary>
    public virtual Guild? Guild { get; set; }

    /// <summary>
    /// All idempotently persisted upstream attacks observed for this guild and season.
    /// </summary>
    public virtual ICollection<GuildRaidAttack> Attacks { get; set; } = new List<GuildRaidAttack>();
}
