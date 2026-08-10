using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Domain.Projects;

/// <summary>
/// Membership of a goal in a project, carrying that project's own priority for the goal (plan §5: priority
/// is per-project, not global — the same goal can belong to multiple projects with a different priority in
/// each).
/// </summary>
public class ProjectGoal
{
    public ProjectId ProjectId { get; set; }

    public GoalId GoalId { get; set; }

    public int Priority { get; set; }

    public GoalEntityType EntityType { get; set; }

    public string EntityId { get; set; } = string.Empty;

    public GoalType GoalType { get; set; }

    public bool OccupiesInFlightSlot { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public virtual Project? Project { get; set; }

    public virtual Goal? Goal { get; set; }
}
