using System.Text.Json.Serialization;

namespace TacticusPlanner.Domain.GuildRaids.Enums;

/// <summary>
/// Identifies the role of the attacked encounter in a Guild Raid set.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GuildRaidEncounterType>))]
public enum GuildRaidEncounterType
{
    /// <summary>
    /// A support encounter whose defeat or health thresholds affect the main boss.
    /// The upstream API calls this value <c>SideBoss</c>.
    /// </summary>
    SideBoss,

    /// <summary>
    /// The main boss encounter that controls progression to the next configured raid set.
    /// The upstream API calls this value <c>Boss</c>.
    /// </summary>
    Boss,
}
