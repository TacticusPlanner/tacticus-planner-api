namespace TacticusPlanner.Domain.GuildRaids.Enums;

/// <summary>
/// Where a past Guild Raid season stands in its capture from the upstream API.
/// </summary>
public enum GuildRaidSeasonCaptureStatus
{
    /// <summary>Still to be captured.</summary>
    Pending,

    /// <summary>Captured after the season ended; never requested again.</summary>
    Final,

    /// <summary>Upstream no longer serves the season.</summary>
    Unavailable,
}
