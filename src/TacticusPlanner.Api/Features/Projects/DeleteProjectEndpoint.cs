using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

public sealed class DeleteProjectRequest
{
    /// <summary>True permanently deletes the goals whose only project this is; false (the default) files them into the
    /// Default project. Goals that also belong to another project are never deleted and keep that membership.</summary>
    [QueryParam]
    public bool? DeleteGoals { get; init; }
}

/// <summary>
/// Permanently deletes a custom project — a hard delete, not a status change; archived or not. The project's
/// <c>project_goals</c> rows cascade away. A goal that belonged to no other project is, in the same transaction,
/// either filed into the Default project (default) or, with <c>deleteGoals=true</c>, hard-deleted exactly as
/// <c>DELETE goals/{goalId}</c> does (leaves the global order, which compacts, and drops out of every dependent's
/// <c>DependsOn</c>). Goals with another membership are never deleted. The Default project cannot be deleted (409
/// <c>defaultProjectCannotBeDeleted</c>).
/// </summary>
public sealed class DeleteProjectEndpoint : Endpoint<DeleteProjectRequest>
{
    public const string DefaultProjectCannotBeDeleted = "defaultProjectCannotBeDeleted";

    public override void Configure()
    {
        Delete("projects/{projectId}");
        Summary(summary =>
        {
            summary.Summary = "Permanently deletes a project.";
            summary.Description = "A goal whose only project this was moves to the Default project, or with "
                + "deleteGoals=true is permanently deleted. Goals that belong to another project are never deleted. "
                + "Moved goals keep their status, target and global priority.";
            summary.Response(StatusCodes.Status204NoContent, "The project was deleted.");
            summary.Response(StatusCodes.Status409Conflict,
                "defaultProjectCannotBeDeleted; current is the project as it stands.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No matching project owned by the caller.");
        });
    }

    public override async Task HandleAsync(DeleteProjectRequest req, CancellationToken ct)
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
            var order = Resolve<GoalOrderService>();
            if (orphanedGoalIds.Count > 0 && req.DeleteGoals == true)
            {
                var deletedIds = orphanedGoalIds.Select(id => id.Value).ToList();
                // The goals and their dependents (no foreign key guards DependsOn, a uuid[]) in one read.
                var related = await db.Goals
                    .Where(entity => orphanedGoalIds.Contains(entity.Id)
                        || entity.DependsOn.Any(id => deletedIds.Contains(id)))
                    .ToListAsync(ct);
                foreach (var goal in related)
                {
                    if (deletedIds.Contains(goal.Id.Value))
                    {
                        order.Release(goal);
                        db.Goals.Remove(goal);
                    }
                    else
                    {
                        goal.DependsOn = goal.DependsOn.Where(id => !deletedIds.Contains(id)).ToList();
                    }
                }
            }
            else if (orphanedGoalIds.Count > 0)
            {
                var defaultProject = await Resolve<ProjectsService>().EnsureDefaultProjectAsync(profileId, ct);
                var now = Resolve<TimeProvider>().GetUtcNow();
                foreach (var goalId in orphanedGoalIds)
                    db.ProjectGoals.Add(ProjectGoalPlanningService.CreateMembership(defaultProject.Id, goalId, now));
            }

            db.Projects.Remove(project);
            await order.CompleteAsync(ct);
            await transaction.CommitAsync(ct);

            await Send.NoContentAsync(ct);
        }, ct);
    }
}
