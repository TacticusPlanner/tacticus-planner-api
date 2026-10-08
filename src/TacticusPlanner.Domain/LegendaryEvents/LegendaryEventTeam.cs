using TacticusPlanner.Domain.Common;

namespace TacticusPlanner.Domain.LegendaryEvents;

/// <summary>A team on one lane of a <see cref="LegendaryEventPlan"/>. <see cref="SortOrder"/> is dense and
/// 0-based within the lane.</summary>
public class LegendaryEventTeam : BaseEntity<LegendaryEventTeamId>
{
    public LegendaryEventPlanId PlanId { get; set; }

    /// <summary>The catalog track key: one of <see cref="LegendaryEventValidation.LaneIds"/>.</summary>
    public required string LaneId { get; set; }

    public required string Name { get; set; }

    public int SortOrder { get; set; }

    public virtual LegendaryEventPlan? Plan { get; set; }

    public virtual ICollection<LegendaryEventTeamMember> Members { get; set; } = new List<LegendaryEventTeamMember>();

    public virtual ICollection<LegendaryEventTeamObjective> Objectives { get; set; } =
        new List<LegendaryEventTeamObjective>();

    public virtual ICollection<LegendaryEventTeamRunDepth> RunDepths { get; set; } =
        new List<LegendaryEventTeamRunDepth>();
}

/// <summary>A unit in a team: positions 0–4 for the line-up, or the single reserve at position 0.</summary>
public class LegendaryEventTeamMember
{
    public LegendaryEventTeamId TeamId { get; set; }

    public bool Reserve { get; set; }

    public int Position { get; set; }

    public required string UnitId { get; set; }

    public virtual LegendaryEventTeam? Team { get; set; }
}

/// <summary>A lane objective the team covers, by the catalog's 0-based objective <c>index</c>.</summary>
public class LegendaryEventTeamObjective
{
    public LegendaryEventTeamId TeamId { get; set; }

    public int ObjectiveIndex { get; set; }

    public virtual LegendaryEventTeam? Team { get; set; }
}

/// <summary>The team's expected clear depth for one run (1–3) of the event.</summary>
public class LegendaryEventTeamRunDepth
{
    public LegendaryEventTeamId TeamId { get; set; }

    public int Run { get; set; }

    public int ExpectedBattleClears { get; set; }

    public LegendaryEventDepthSource Source { get; set; }

    /// <summary>When this run's depth was last written.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    public virtual LegendaryEventTeam? Team { get; set; }
}

public enum LegendaryEventDepthSource
{
    Estimate,
    Manual,
}
