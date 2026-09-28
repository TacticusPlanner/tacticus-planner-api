using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Domain.Projects;

/// <summary>
/// Membership of a goal in a project. A project has no order of its own: goals are ordered by
/// <see cref="Goal.GlobalPriority"/>, and project reads are filtered projections of that order.
/// </summary>
public class ProjectGoal
{
    public ProjectId ProjectId { get; set; }

    public GoalId GoalId { get; set; }

    public GoalEntityType EntityType { get; set; }

    public string EntityId { get; set; } = string.Empty;

    public GoalType GoalType { get; set; }

    public bool OccupiesInFlightSlot { get; set; }

    /// <summary>Normalized end target (<see cref="Goals.RankTargetKey"/>) for a Rank goal, null for every other
    /// type. Denormalized from the goal's config so a partial unique index can enforce Rank target
    /// occupancy; kept in sync by every target mutation.</summary>
    public string? RankTargetKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public virtual Project? Project { get; set; }

    public virtual Goal? Goal { get; set; }
}
