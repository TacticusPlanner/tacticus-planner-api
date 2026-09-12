using System.Text.Json.Serialization;

namespace TacticusPlanner.Domain.GuildRaids.Enums;

/// <summary>
/// Describes whether a successful upstream observation found an active Guild Raid season.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GuildRaidObservationState>))]
public enum GuildRaidObservationState
{
    /// <summary>
    /// An active season was returned and <c>ActiveSeasonId</c> identifies its persisted snapshot.
    /// Serialized as <c>active</c>.
    /// </summary>
    [JsonStringEnumMemberName("active")]
    Active,

    /// <summary>
    /// The upstream request succeeded but reported that no season was active.
    /// Serialized as <c>noActiveSeason</c>.
    /// </summary>
    [JsonStringEnumMemberName("noActiveSeason")]
    NoActiveSeason,
}
