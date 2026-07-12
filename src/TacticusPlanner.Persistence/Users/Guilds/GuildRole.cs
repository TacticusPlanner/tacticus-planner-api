namespace TacticusPlanner.Persistence.Users.Guilds;

/// <summary>
/// Mirrors <c>TacticusPlanner.TacticusApi.Models.Guild.GuildRole</c> (the upstream Tacticus API's role
/// enum), but is declared independently so this persistence project does not need to reference the
/// TacticusApi project. Mapping between the two happens once, in the application-layer sync service.
/// </summary>
public enum GuildRole
{
    Member,
    Officer,
    CoLeader,
    Leader,
}
