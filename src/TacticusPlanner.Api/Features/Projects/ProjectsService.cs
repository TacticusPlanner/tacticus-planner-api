using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>
/// Shared project logic used by both the Goals and Projects features: every goal must belong to at least
/// one project (plan §5), so goal creation needs the same default-project provisioning that the Projects
/// list endpoint exposes explicitly. Also serves as the idempotent fallback for profiles provisioned
/// before <c>GetCurrentUserEndpoint</c> started seeding the default project directly — see that
/// endpoint's <c>ProvisionAccountAsync</c>, which is now the primary seeding path.
/// </summary>
public sealed class ProjectsService(PlannerDbContext db)
{
    /// <summary>Gets the profile's default project ("My Goals"), creating it if it does not exist yet,
    /// and makes it the profile's active plan if none is set yet.</summary>
    public async Task<Project> EnsureDefaultProjectAsync(ProfileId profileId, CancellationToken ct)
    {
        var existing = await db.Projects.FirstOrDefaultAsync(entity => entity.ProfileId == profileId && entity.IsDefault, ct);

        var project = existing;
        if (project is null)
        {
            project = new Project
            {
                Id = ProjectId.From(Guid.CreateVersion7()),
                ProfileId = profileId,
                Name = "My Goals",
                Status = ProjectStatus.Active,
                IsDefault = true,
            };

            db.Projects.Add(project);
        }

        var profile = await db.Profiles.FirstAsync(entity => entity.Id == profileId, ct);
        profile.ActiveProjectId ??= project.Id;

        // A no-op when neither the project nor the profile actually changed — SaveChanges only issues
        // statements for entities the change tracker marked dirty.
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
