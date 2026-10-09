namespace TacticusPlanner.Domain.LegendaryEvents;

/// <summary>How many battles the team is expected to clear in one run (1–3) of the event. One row per
/// stored run; <see cref="RecordedAt"/> is refreshed on every write so later history can pair the depth
/// with the roster snapshot nearest to it.</summary>
public class LegendaryEventTeamRunDepth
{
    public LegendaryEventTeamId TeamId { get; set; }

    public int Run { get; set; }

    public int ExpectedBattleClears { get; set; }

    public LegendaryEventDepthSource Source { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public virtual LegendaryEventTeam? Team { get; set; }
}
