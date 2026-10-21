using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>
/// Maps between <see cref="Project"/> and its API request/response shapes.
/// </summary>
public sealed class ProjectMapper : Mapper<CreateProjectRequest, ProjectSummaryResponse, Project>
{
    public override Project ToEntity(CreateProjectRequest r) => new()
    {
        Name = r.Name.Trim(),
        Description = r.Description,
        Color = r.Color,
        Status = ProjectStatus.Active,
        TargetKind = r.Target is null ? null : ProjectTargetKind.LegendaryEvent,
        TargetEventId = r.Target?.EventId,
    };

    public ProjectSummaryResponse ToSummary(Project project, List<Guid> goalIds) => new(
        project.Id.Value,
        project.Name,
        project.Description,
        project.Color,
        project.Status.ToString(),
        project.Type == ProjectType.Default,
        ProjectTargets.ToDto(project),
        project.Revision,
        project.CreatedAt,
        project.UpdatedAt,
        goalIds
    );
}

/// <param name="GoalIds">The member goals matching the list's archived selector, in global order (in-flight first
/// by position, then the rest newest first).</param>
public sealed record ProjectSummaryResponse(
    Guid ProjectId,
    string Name,
    string? Description,
    string? Color,
    string Status,
    bool IsDefault,
    ProjectTargetDto? Target,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<Guid> GoalIds
);

public static class ProjectGoalQueries
{
    /// <summary>Member goal ids per project in global order, for the projects with <paramref name="projectIds"/>
    /// (all of the caller's when null), in one query. <paramref name="archived"/> is the goals list's selector:
    /// Archived goals only, or every other goal.</summary>
    public static async Task<Dictionary<ProjectId, List<Guid>>> GoalIdsByProjectAsync(
        this PlannerDbContext db, bool archived, IReadOnlyCollection<ProjectId>? projectIds, CancellationToken ct)
    {
        var memberships = db.ProjectGoals.AsQueryable();
        if (projectIds is not null)
            memberships = memberships.Where(entry => projectIds.Contains(entry.ProjectId));
        var rows = await memberships
            .Join(db.Goals, entry => entry.GoalId, goal => goal.Id, (entry, goal) => new { entry.ProjectId, Goal = goal })
            .Where(row => archived ? row.Goal.Status == GoalStatus.Archived : row.Goal.Status != GoalStatus.Archived)
            .OrderBy(row => row.Goal.GlobalPriority == null)
            .ThenBy(row => row.Goal.GlobalPriority)
            .ThenByDescending(row => row.Goal.CreatedAt)
            .ThenBy(row => row.Goal.Id)
            .Select(row => new { row.ProjectId, GoalId = row.Goal.Id.Value })
            .ToListAsync(ct);
        return rows
            .GroupBy(row => row.ProjectId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.GoalId).ToList());
    }

    /// <summary>One project's non-archived member goal ids in global order (the default list selector).</summary>
    public static async Task<List<Guid>> GoalIdsAsync(this PlannerDbContext db, ProjectId projectId, CancellationToken ct) =>
        (await db.GoalIdsByProjectAsync(archived: false, [projectId], ct)).GetValueOrDefault(projectId, []);
}
