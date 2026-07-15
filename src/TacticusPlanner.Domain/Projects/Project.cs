using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Domain.Projects;

/// <summary>
/// A planning container: the unit goals are grouped, prioritized, and bulk-managed within (plan §3/§5).
/// Doubles as a "plan" — a profile's current active plan is the project referenced by its
/// <see cref="Profiles.Profile.ActiveProjectId"/>, and that project's goals are the set that will (in a
/// future phase) drive Daily-Raids recommendations. Every goal must belong to at least one project; each
/// profile is provisioned a default project on first access.
/// </summary>
public class Project : BaseEntity<ProjectId>, IRevisionedEntity
{
    public long Revision { get; set; }

    public ProfileId ProfileId { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public string? Color { get; set; }

    public ProjectStatus Status { get; set; }

    /// <summary>True for the profile's auto-provisioned default project ("My Goals") — informational only;
    /// it can be renamed but cannot be archived.</summary>
    public bool IsDefault { get; set; }

    public virtual Profile? Profile { get; set; }

    public virtual ICollection<ProjectGoal> ProjectGoals { get; set; } = new List<ProjectGoal>();

    public virtual ICollection<ProjectTeam> ProjectTeams { get; set; } = new List<ProjectTeam>();
}
