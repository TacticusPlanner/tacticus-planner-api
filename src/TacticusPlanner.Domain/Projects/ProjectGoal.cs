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

    public DateTimeOffset CreatedAt { get; set; }

    public virtual Project? Project { get; set; }

    public virtual Goal? Goal { get; set; }
}
