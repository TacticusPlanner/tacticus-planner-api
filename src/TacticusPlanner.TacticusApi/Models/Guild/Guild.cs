namespace TacticusPlanner.TacticusApi.Models.Guild;

public class Guild
{
    public Guid GuildId { get; set; }
    public string GuildTag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; }
    public List<GuildMember> Members { get; set; } = new();
    public List<int> GuildRaidSeasons { get; set; } = new();
}
