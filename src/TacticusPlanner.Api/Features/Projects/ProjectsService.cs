using Microsoft.EntityFrameworkCore;
using Npgsql;
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
    /// <summary>Gets the profile's default project ("My Goals"), creating it if it does not exist yet. A
    /// concurrent request may create it first; the unique Default index turns that race into a re-read.</summary>
    public async Task<Project> EnsureDefaultProjectAsync(ProfileId profileId, CancellationToken ct)
    {
        var existing = await FindDefaultAsync(profileId, ct);
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
            Type = ProjectType.Default,
        };

        db.Projects.Add(project);
        try
        {
            await db.SaveChangesAsync(ct);
            return project;
        }
        catch (DbUpdateException exception) when (IsDefaultProjectConflict(exception))
        {
            // Another request created it first (ix_projects_profile_id_default): use theirs.
            db.Entry(project).State = EntityState.Detached;
            return await FindDefaultAsync(profileId, ct)
                ?? throw new InvalidOperationException("The default project vanished after a unique conflict.");
        }
    }

    private Task<Project?> FindDefaultAsync(ProfileId profileId, CancellationToken ct) =>
        db.Projects.FirstOrDefaultAsync(
            entity => entity.ProfileId == profileId && entity.Type == ProjectType.Default, ct);

    private static bool IsDefaultProjectConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
            && postgres.ConstraintName == "ix_projects_profile_id_default";
}
