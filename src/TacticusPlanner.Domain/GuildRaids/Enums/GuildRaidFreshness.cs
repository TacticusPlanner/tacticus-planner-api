using System.Text.Json.Serialization;

namespace TacticusPlanner.Domain.GuildRaids.Enums;

/// <summary>
/// Describes whether the most recent refresh attempt for this guild succeeded.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GuildRaidFreshness>))]
public enum GuildRaidFreshness
{
    /// <summary>
    /// The most recent refresh attempt succeeded; this is the latest known observation.
    /// Serialized as <c>fresh</c>.
    /// </summary>
    [JsonStringEnumMemberName("fresh")]
    Fresh,

    /// <summary>
    /// The most recent refresh attempt failed and this is a retained fallback from an earlier successful observation.
    /// Serialized as <c>stale</c>.
    /// </summary>
    [JsonStringEnumMemberName("stale")]
    Stale,
}
