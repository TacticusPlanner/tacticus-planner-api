namespace TacticusPlanner.TacticusApi.Models.Guild;

public class GuildMember
{
    public Guid UserId { get; set; }
    public GuildRole Role { get; set; }
    public int Level { get; set; }
    public long? LastActivityOn { get; set; }
}
