namespace TacticusPlanner.Domain.Projects;

/// <summary>
/// Membership of a team in a project — projects span goals and teams, not just goals (plan §2/§3.3).
/// <see cref="TeamId"/> is a loose reference (no FK, no Vogen wrapper) rather than a foreign key, because
/// no Team domain entity exists yet; tighten this once the Teams feature lands.
/// </summary>
public class ProjectTeam
{
    public ProjectId ProjectId { get; set; }

    public Guid TeamId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public virtual Project? Project { get; set; }
}
