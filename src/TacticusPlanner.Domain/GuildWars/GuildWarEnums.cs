using System.Text.Json.Serialization;

namespace TacticusPlanner.Domain.GuildWars;

/// <summary>A game rarity, serialized with the game's names (<c>guild-war-offense</c>).</summary>
public enum GuildWarRarity
{
    [JsonStringEnumMemberName("Common")] Common,
    [JsonStringEnumMemberName("Uncommon")] Uncommon,
    [JsonStringEnumMemberName("Rare")] Rare,
    [JsonStringEnumMemberName("Epic")] Epic,
    [JsonStringEnumMemberName("Legendary")] Legendary,
    [JsonStringEnumMemberName("Mythic")] Mythic,
}

public enum GuildWarAttackResult
{
    [JsonStringEnumMemberName("won")] Won,
    [JsonStringEnumMemberName("lost")] Lost,
}

/// <summary>How an enemy unit ended the attack.</summary>
public enum GuildWarEnemyUnitState
{
    [JsonStringEnumMemberName("killed")] Killed,
    [JsonStringEnumMemberName("damaged")] Damaged,
}

/// <summary>Why an attack is refused, in the order the rules are checked (<c>guild-war-offense</c>, "A logged attack
/// follows the war's rules"). Served as the problem's <c>issueCode</c>.</summary>
public enum GuildWarAttackRuleCode
{
    [JsonStringEnumMemberName("guildWarUnknownZone")] UnknownZone,
    [JsonStringEnumMemberName("guildWarUnknownUnit")] UnknownUnit,
    [JsonStringEnumMemberName("guildWarUnitDeployed")] UnitDeployed,
    [JsonStringEnumMemberName("guildWarTokensSpent")] TokensSpent,
}
