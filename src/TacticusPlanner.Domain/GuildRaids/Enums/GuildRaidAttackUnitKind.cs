using System.Text.Json.Serialization;

namespace TacticusPlanner.Domain.GuildRaids.Enums;

/// <summary>
/// Identifies which upstream team slot collection supplied a persisted attack unit.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GuildRaidAttackUnitKind>))]
public enum GuildRaidAttackUnitKind
{
    /// <summary>
    /// A character from the upstream <c>heroDetails</c> collection.
    /// Serialized as <c>hero</c>.
    /// </summary>
    [JsonStringEnumMemberName("hero")]
    Hero,

    /// <summary>
    /// The optional unit from the upstream <c>machineOfWarDetails</c> field.
    /// Serialized as <c>machineOfWar</c>.
    /// </summary>
    [JsonStringEnumMemberName("machineOfWar")]
    MachineOfWar,
}
