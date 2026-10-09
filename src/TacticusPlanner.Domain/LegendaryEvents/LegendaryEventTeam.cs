using TacticusPlanner.Domain.Common;

namespace TacticusPlanner.Domain.LegendaryEvents;

/// <summary>A team on one lane of a plan: ordered members, an optional reserve, the objective indexes it
/// covers and its clear depth per run. <see cref="SortOrder"/> is dense and 0-based within the lane.</summary>
public class LegendaryEventTeam : BaseEntity<LegendaryEventTeamId>
{
    public LegendaryEventPlanId PlanId { get; set; }

    /// <summary>One of <see cref="LegendaryEventPlanRules.LaneIds"/>; never changes after creation.</summary>
    public required string LaneId { get; set; }

    public required string Name { get; set; }

    public int SortOrder { get; set; }

    public virtual List<LegendaryEventTeamMember> Members { get; set; } = [];

    public virtual List<LegendaryEventTeamObjective> Objectives { get; set; } = [];

    public virtual List<LegendaryEventTeamRunDepth> RunDepths { get; set; } = [];

    public virtual LegendaryEventPlan? Plan { get; set; }
}
