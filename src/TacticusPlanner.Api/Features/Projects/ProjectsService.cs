using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>
/// Shared project logic used by both the Goals and Projects features: every goal must belong to at least
/// one project (plan §5), so goal creation needs the same default-project provisioning that the Projects
/// list endpoint exposes explicitly.
/// </summary>
public sealed class ProjectsService(PlannerDbContext db)
{
    /// <summary>Gets the profile's default project ("My Goals"), creating it — as the profile's first and
    /// therefore initial active plan — if it does not exist yet. Mirrors the lazy Account/Profile
    /// provisioning in <c>GetCurrentUserEndpoint</c>.</summary>
    public async Task<Project> EnsureDefaultProjectAsync(ProfileId profileId, CancellationToken ct)
    {
        var existing = await db.Projects.FirstOrDefaultAsync(entity => entity.ProfileId == profileId && entity.IsDefault, ct);
        if (existing is not null)
        {
            return existing;
        }

        var project = new Project
        {
            Id = ProjectId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            Name = "My Goals",
            Status = ProjectStatus.Active,
            IsActivePlan = true,
            IsDefault = true,
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        return project;
    }

    /// <summary>The next priority value to append a goal at the bottom of a project's ordering.</summary>
    public async Task<int> GetNextPriorityAsync(ProjectId projectId, CancellationToken ct)
    {
        var max = await db.ProjectGoals
            .Where(entity => entity.ProjectId == projectId)
            .Select(entity => (int?)entity.Priority)
            .MaxAsync(ct);

        return (max ?? 0) + 1;
    }
}
