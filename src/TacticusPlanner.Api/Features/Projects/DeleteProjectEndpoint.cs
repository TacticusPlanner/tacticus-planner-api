using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>
/// Permanently deletes a custom project — a hard delete, not a status change; archived or not. Its goals are kept:
/// the project's <c>project_goals</c> rows cascade away, and every goal that belonged to no other project is filed
/// into the Default project in the same transaction, so every goal keeps at least one membership. Goal status,
/// target and global priority never change. The Default project cannot be deleted (409
/// <c>defaultProjectCannotBeDeleted</c>).
/// </summary>
public sealed class DeleteProjectEndpoint : EndpointWithoutRequest
{
    public const string DefaultProjectCannotBeDeleted = "defaultProjectCannotBeDeleted";

    public override void Configure()
    {
        Delete("projects/{projectId}");
        Summary(summary =>
        {
            summary.Summary = "Permanently deletes a project.";
            summary.Description = "Goals are never deleted: a goal whose only project this was moves to the "
                + "Default project. Never changes any goal's status, target or global priority.";
            summary.Response(StatusCodes.Status204NoContent, "The project was deleted.");
            summary.Response(StatusCodes.Status409Conflict,
                "defaultProjectCannotBeDeleted; current is the project as it stands.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No matching project owned by the caller.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is not { } profileId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var projectId = ProjectId.From(Route<Guid>("projectId"));
        var db = Resolve<PlannerDbContext>();

        // Under the profile lock, like every membership change: a concurrent membership edit has either committed
        // and is visible to the orphan read below, or waits until this delete has committed.
        await Resolve<ProjectGoalPlanningService>().ExecuteLockedMutationAsync(async transaction =>
        {
            // Scoped to the caller's profile by PlannerDbContext's global query filter.
            var project = await db.Projects.FirstOrDefaultAsync(entity => entity.Id == projectId, ct);
            if (project is null)
            {
                await this.SendNotFoundProblemAsync(ct);
                return;
            }

            if (project.Type == ProjectType.Default)
            {
                await this.SendConflictAsync(
                    DefaultProjectCannotBeDeleted, "The default project cannot be deleted.",
                    new ProjectMapper().ToSummary(project, await db.GoalIdsAsync(project.Id, ct)), ct);
                return;
            }

            // Goals with no membership outside this project would be left in none once it is gone.
            var orphanedGoalIds = await db.ProjectGoals
                .Where(entity => entity.ProjectId == projectId)
                .Where(entity => !db.ProjectGoals.Any(other => other.GoalId == entity.GoalId && other.ProjectId != projectId))
                .Select(entity => entity.GoalId)
                .ToListAsync(ct);
            if (orphanedGoalIds.Count > 0)
            {
                var defaultProject = await Resolve<ProjectsService>().EnsureDefaultProjectAsync(profileId, ct);
                var now = Resolve<TimeProvider>().GetUtcNow();
                foreach (var goalId in orphanedGoalIds)
                    db.ProjectGoals.Add(ProjectGoalPlanningService.CreateMembership(defaultProject.Id, goalId, now));
            }

            db.Projects.Remove(project);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            await Send.NoContentAsync(ct);
        }, ct);
    }
}
