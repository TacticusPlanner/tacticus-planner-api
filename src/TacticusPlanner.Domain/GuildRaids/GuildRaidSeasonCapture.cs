using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.GuildRaids.Enums;
using TacticusPlanner.Domain.Guilds;

namespace TacticusPlanner.Domain.GuildRaids;

/// <summary>
/// Capture progress of one past season of one guild. Separate from <see cref="GuildRaidSeason"/>, which needs a
/// season config id that is unknown until the season's first response.
/// </summary>
public class GuildRaidSeasonCapture : BaseEntity
{
    public GuildId GuildId { get; set; }

    /// <summary>Upstream season number; the primary key together with <see cref="GuildId"/>.</summary>
    public int SeasonNumber { get; set; }

    public GuildRaidSeasonCaptureStatus Status { get; set; }

    /// <summary>How often upstream answered 404 for this season.</summary>
    public int NotFoundAttempts { get; set; }

    /// <summary>Malformed entries left out of the last response.</summary>
    public int SkippedEntryCount { get; set; }

    public DateTimeOffset? LastAttemptedAt { get; set; }

    /// <summary>When the season became <see cref="GuildRaidSeasonCaptureStatus.Final"/>.</summary>
    public DateTimeOffset? FinalizedAt { get; set; }

    public virtual Guild? Guild { get; set; }
}
