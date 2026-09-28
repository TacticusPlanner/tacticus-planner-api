using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Domain.Projects;

/// <summary>
/// A planning container: the unit goals are grouped, prioritized, and bulk-managed within (plan §3/§5).
/// Every goal must belong to at least one project; each profile has exactly one default project
/// (<see cref="ProjectType.Default"/>, "My Goals"), provisioned on first access, which cannot be archived
/// and is where a goal lands when it would otherwise have no project. There is no "active" project:
/// planning runs over the account-wide goal order.
/// </summary>
public class Project : BaseEntity<ProjectId>, IRevisionedEntity
{
    public long Revision { get; set; }

    public ProfileId ProfileId { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public string? Color { get; set; }

    public ProjectStatus Status { get; set; }

    public ProjectType Type { get; set; }

    public virtual Profile? Profile { get; set; }

    public virtual ICollection<ProjectGoal> ProjectGoals { get; set; } = new List<ProjectGoal>();
}
