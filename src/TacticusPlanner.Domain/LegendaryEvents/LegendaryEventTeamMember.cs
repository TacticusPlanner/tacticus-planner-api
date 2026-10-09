namespace TacticusPlanner.Domain.LegendaryEvents;

/// <summary>One unit of a team. Keyed by <c>(team, reserve, position)</c>: non-reserve members hold
/// positions 0–4, the single reserve holds position 0 with <see cref="Reserve"/> set. A unit appears at
/// most once per team (members and reserve together).</summary>
public class LegendaryEventTeamMember
{
    public LegendaryEventTeamId TeamId { get; set; }

    public bool Reserve { get; set; }

    public int Position { get; set; }

    public required string UnitId { get; set; }

    public virtual LegendaryEventTeam? Team { get; set; }
}
