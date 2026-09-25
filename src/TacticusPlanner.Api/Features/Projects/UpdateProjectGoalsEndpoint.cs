using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>
/// Replaces a project's goal membership (plan §5) in one call. Every goal must belong to at least one
/// project, so a goal cannot be removed from its only remaining project — such a removal is rejected
/// wholesale (400) rather than silently orphaning the goal. Does NOT accept caller-authored priority (plan:
/// <c>add-inline-goal-reprioritize</c> — priority is set exclusively via <see cref="UpdateProjectGoalOrderEndpoint"/>
/// now that priority is flat per-goal, not unit-grouped, so nothing downstream reconciles an arbitrary
/// submitted value the way <see cref="ProjectGoalPlanningService.NormalizeAsync"/>'s old per-unit
/// regrouping incidentally did): an existing member's priority is left untouched regardless of what
/// <see cref="ProjectGoalEntryRequest.Priority"/> is submitted for it, and a newly added member is
/// appended after the project's current in-flight goals, same as <see cref="CreateGoalEndpoint"/>.
/// </summary>
public sealed class UpdateProjectGoalsEndpoint : Endpoint<UpdateProjectGoalsRequest, ProjectGoalsResponse>
{
    public override void Configure()
    {
        Put("me/projects/{projectId}/goals");
        Summary(summary =>
        {
            summary.Summary = "Replaces a project's goal membership and priority ordering.";
            summary.Response<ProjectGoalsResponse>(StatusCodes.Status200OK, "The project's updated goal membership.");
            summary.Response(StatusCodes.Status400BadRequest, "An unknown goal id, or a removal that would leave a goal in no project.");
            summary.Response<ProjectGoalSlotConflictResponse>(StatusCodes.Status409Conflict,
                "The requested membership contains an occupied active or paused goal slot.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No matching project owned by the caller.");
        });
    }

    public override async Task HandleAsync(UpdateProjectGoalsRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var projectId = ProjectId.From(Route<Guid>("projectId"));
        var db = Resolve<PlannerDbContext>();

        // Both queries below are scoped to the caller's profile by PlannerDbContext's global query filter.
        var project = await db.Projects.FirstOrDefaultAsync(entity => entity.Id == projectId, ct);
        if (project is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var planning = Resolve<ProjectGoalPlanningService>();
        await planning.ExecuteLockedMutationAsync([projectId], async transaction =>
        {

            var requestedGoalIds = req.Goals.Select(entry => GoalId.From(entry.GoalId)).ToHashSet();

            var ownedGoals = await db.Goals
                .Where(entity => requestedGoalIds.Contains(entity.Id))
                .ToListAsync(ct);

            if (ownedGoals.Count != requestedGoalIds.Count)
            {
                AddError(request => request.Goals, "One or more goals do not exist or are not owned by the caller.");
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
            }

            var duplicateSlot = ownedGoals
                .Where(goal => goal.Status is GoalStatus.Active or GoalStatus.Paused)
                .GroupBy(goal => new
                {
                    goal.EntityType,
                    goal.EntityId,
                    goal.GoalType,
                    RankTargetKey = RankTargetKey.For(goal.GoalType, goal.Config),
                })
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateSlot is not null)
            {
                var existing = duplicateSlot.First();
                HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                await HttpContext.Response.WriteAsJsonAsync(new ProjectGoalSlotConflictResponse(
                    "projectGoalSlotOccupied",
                    ProjectGoalPlanningService.ConflictMessage(
                        project.Name, existing.GoalType, duplicateSlot.Key.RankTargetKey),
                    project.Id.Value, project.Name, existing.EntityType.ToString(),
                    existing.EntityId, existing.GoalType.ToString(), existing.Id.Value,
                    duplicateSlot.Key.RankTargetKey), ct);
                return;
            }

            var existingMemberships = await db.ProjectGoals
                .Where(entity => entity.ProjectId == projectId)
                .ToListAsync(ct);

            var toRemove = existingMemberships.Where(entity => !requestedGoalIds.Contains(entity.GoalId)).ToList();
            if (toRemove.Count > 0)
            {
                var removedGoalIds = toRemove.Select(entity => entity.GoalId).ToHashSet();
                var otherMembershipCounts = await db.ProjectGoals
                    .Where(entity => removedGoalIds.Contains(entity.GoalId) && entity.ProjectId != projectId)
                    .GroupBy(entity => entity.GoalId)
                    .Select(group => new { GoalId = group.Key, Count = group.Count() })
                    .ToListAsync(ct);

                var orphaned = removedGoalIds
                    .Except(otherMembershipCounts.Where(entry => entry.Count > 0).Select(entry => entry.GoalId))
                    .ToList();

                if (orphaned.Count > 0)
                {
                    AddError(request => request.Goals, "Cannot remove a goal from its only remaining project.");
                    await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                    return;
                }

                db.ProjectGoals.RemoveRange(toRemove);
            }

            // Priority is never taken from the request (see this endpoint's class doc): an existing
            // member keeps its current stored priority untouched, and a newly added member is appended
            // via GetNextPriorityAsync (same as CreateGoalEndpoint) — NormalizeAsync below then produces
            // the final, definitive values for the whole project regardless.
            var existingByGoalId = existingMemberships.ToDictionary(entity => entity.GoalId);
            var goalsById = ownedGoals.ToDictionary(goal => goal.Id);
            var projects = Resolve<ProjectsService>();
            // Queried once, then incremented locally — GetNextPriorityAsync re-queried per entry would
            // return the same value for every not-yet-saved addition, since it reads the database, not
            // this change tracker's pending inserts.
            var nextPriority = await projects.GetNextPriorityAsync(projectId, ct);
            foreach (var entry in req.Goals)
            {
                var goalId = GoalId.From(entry.GoalId);
                if (!existingByGoalId.ContainsKey(goalId))
                {
                    db.ProjectGoals.Add(ProjectGoalPlanningService.CreateMembership(
                        project, goalsById[goalId], nextPriority++, DateTimeOffset.UtcNow));
                }
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (GoalConflictDetection.IsProjectSlotConflict(ex))
            {
                var conflict = await planning.FindConflictAfterFailedSaveAsync(
                    transaction,
                    ownedGoals
                        .Where(goal => goal.Status is GoalStatus.Active or GoalStatus.Paused)
                        .Select(goal => new ProjectGoalSlotLookup(
                            [projectId],
                            goal.EntityType,
                            goal.EntityId,
                            goal.GoalType,
                            RankTargetKey.For(goal.GoalType, goal.Config),
                            goal.Id)),
                    ct) ?? throw new InvalidOperationException(
                        "The project slot constraint failed but no conflicting membership was found.", ex);
                HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                await HttpContext.Response.WriteAsJsonAsync(conflict, ct);
                return;
            }

            await planning.NormalizeAsync([projectId], ct);
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);

            var updated = await db.ProjectGoals
                .AsNoTracking()
                .Where(entity => entity.ProjectId == projectId)
                .OrderBy(entity => entity.Priority)
                .Select(entity => new ProjectGoalEntryResponse(entity.GoalId.Value, entity.Priority))
                .ToListAsync(ct);

            await Send.OkAsync(new ProjectGoalsResponse(updated), ct);
        }, ct);
    }
}

public sealed record UpdateProjectGoalsRequest(List<ProjectGoalEntryRequest> Goals);

/// <summary>No <c>Priority</c> field (see the endpoint's class doc for why) — removed rather than kept
/// and ignored, since a field with no legitimate use is worse than no field.</summary>
public sealed record ProjectGoalEntryRequest(Guid GoalId);

public sealed record ProjectGoalsResponse(List<ProjectGoalEntryResponse> Goals);

public sealed record ProjectGoalEntryResponse(Guid GoalId, int Priority);
