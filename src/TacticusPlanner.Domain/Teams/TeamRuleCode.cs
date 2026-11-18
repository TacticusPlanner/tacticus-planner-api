using System.Text.Json.Serialization;

namespace TacticusPlanner.Domain.Teams;

/// <summary>
/// A team rule a lineup breaks (<c>personal-teams</c>, "Every save satisfies the team rules"). One closed enum in the
/// contract, used by a team's <c>issues</c> and by a refused save, so a client generated from the contract knows every
/// code. Declared in the order of the spec's rules table, which is also the order <see cref="TeamRules.Evaluate"/>
/// reports them in.
/// </summary>
public enum TeamRuleCode
{
    [JsonStringEnumMemberName("nameLength")] NameLength,
    [JsonStringEnumMemberName("noMode")] NoMode,
    [JsonStringEnumMemberName("noUnits")] NoUnits,
    [JsonStringEnumMemberName("unknownUnit")] UnknownUnit,
    [JsonStringEnumMemberName("modeNotCombinable")] ModeNotCombinable,
    [JsonStringEnumMemberName("tooManyCoreCharacters")] TooManyCoreCharacters,
    [JsonStringEnumMemberName("tooManyMows")] TooManyMows,
    [JsonStringEnumMemberName("mowsNotAllowed")] MowsNotAllowed,
    [JsonStringEnumMemberName("campaignRequired")] CampaignRequired,
    [JsonStringEnumMemberName("unknownCampaign")] UnknownCampaign,
    [JsonStringEnumMemberName("coreCharacterMissing")] CoreCharacterMissing,
    [JsonStringEnumMemberName("factionNotAllowed")] FactionNotAllowed,
    [JsonStringEnumMemberName("incursionRequired")] IncursionRequired,
    [JsonStringEnumMemberName("unknownIncursion")] UnknownIncursion,
    [JsonStringEnumMemberName("noSharedIncursionFaction")] NoSharedIncursionFaction,
}
