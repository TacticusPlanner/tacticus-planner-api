namespace TacticusPlanner.TacticusApi.Models.GuildRaid;

public class GuildRaidResponse
{
    public int Season { get; set; }
    public string SeasonConfigId { get; set; } = string.Empty;
    public List<GuildRaidEntry> Entries { get; set; } = new();
}
