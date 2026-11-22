using System.Text.Json.Serialization;

namespace TacticusPlanner.Domain.Teams;

/// <summary>
/// The rarity a team is prepared for (<c>personal-teams</c>, design D13). Stored and returned as sent so clients can
/// preview the lineup at that rarity; no team rule reads it. Serialized with the game's rarity names.
/// </summary>
public enum TeamRarityCap
{
    [JsonStringEnumMemberName("Common")] Common,
    [JsonStringEnumMemberName("Uncommon")] Uncommon,
    [JsonStringEnumMemberName("Rare")] Rare,
    [JsonStringEnumMemberName("Epic")] Epic,
    [JsonStringEnumMemberName("Legendary")] Legendary,
    [JsonStringEnumMemberName("Mythic")] Mythic,
}
