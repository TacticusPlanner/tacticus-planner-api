using System.Text.Json.Serialization;

namespace TacticusPlanner.Domain.GuildRaids.Enums;

/// <summary>
/// Identifies the rarity-based difficulty reported for a Guild Raid encounter.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GuildRaidDifficulty>))]
public enum GuildRaidDifficulty
{
    /// <summary>The Common difficulty.</summary>
    Common,

    /// <summary>The Uncommon difficulty.</summary>
    Uncommon,

    /// <summary>The Rare difficulty.</summary>
    Rare,

    /// <summary>The Epic difficulty.</summary>
    Epic,

    /// <summary>The Legendary difficulty.</summary>
    Legendary,

    /// <summary>The Mythic difficulty.</summary>
    Mythic,
}
