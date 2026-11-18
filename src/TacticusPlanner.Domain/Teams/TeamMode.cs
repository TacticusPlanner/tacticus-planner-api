using System.Text.Json.Serialization;

namespace TacticusPlanner.Domain.Teams;

/// <summary>A game mode a team is built for (<c>personal-teams</c>). Stored as its C# name, served camelCase.</summary>
public enum TeamMode
{
    [JsonStringEnumMemberName("warOffense")] WarOffense,
    [JsonStringEnumMemberName("warDefense")] WarDefense,
    [JsonStringEnumMemberName("guildRaid")] GuildRaid,
    [JsonStringEnumMemberName("tournamentArena")] TournamentArena,
    [JsonStringEnumMemberName("horde")] Horde,
    [JsonStringEnumMemberName("campaign")] Campaign,
    [JsonStringEnumMemberName("incursion")] Incursion,
}
