using System.Text.Json.Serialization;

namespace TacticusPlanner.Domain.GuildRaids.Enums;

/// <summary>
/// Identifies how damage was applied in an upstream Guild Raid entry.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GuildRaidDamageType>))]
public enum GuildRaidDamageType
{
    /// <summary>Damage applied by a Guild Raid bomb rather than a combat attempt.</summary>
    Bomb,

    /// <summary>Damage applied by a normal combat attempt.</summary>
    Battle,
}
