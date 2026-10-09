namespace TacticusPlanner.Domain.LegendaryEvents;

/// <summary>An objective the team covers, identified by the lane's catalog objective <c>index</c> (the
/// catalog has no objective string id; the synced progress chunk uses the same index).</summary>
public class LegendaryEventTeamObjective
{
    public LegendaryEventTeamId TeamId { get; set; }

    public int ObjectiveIndex { get; set; }

    public virtual LegendaryEventTeam? Team { get; set; }
}
