using FastEndpoints;
using FluentValidation;
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
/// wholesale (400, naming the blocked goals) rather than silently orphaning the goal. The request carries
/// the membership the client reviewed (<c>expectedGoalIds</c>); if it no longer matches the current
/// membership under the project lock the whole save is rejected as stale (409). Membership never changes goal priority: a
/// project holds no order of its own, and every member keeps its account-wide position (see
/// <see cref="GoalOrderService"/>; the only project-level write is <see cref="UpdateProjectGoalOrderEndpoint"/>).
/// </summary>
public sealed class UpdateProjectGoalsEndpoint : Endpoint<UpdateProjectGoalsRequest, ProjectGoalsResponse>
{
    public override void Configure()
    {
        Put("me/projects/{projectId}/goals");
        Summary(summary =>
        {
            summary.Summary = "Replaces a project's goal membership.";
            summary.Description = "Atomic: any rejection changes nothing. Never changes any goal's global priority or "
                + "the goal-order revision. expectedGoalIds is the membership the client reviewed; it is compared "
                + "with the current membership under the project lock.";
            summary.Response<ProjectGoalsResponse>(StatusCodes.Status200OK,
                "The project's updated members in global-priority order.");
            summary.Response<ProjectLastMembershipResponse>(StatusCodes.Status400BadRequest,
                "A removal that would leave goals in no project (issueCode lastProjectMembership, naming the "
                + "blocked goal ids), or an unknown goal id (a validation error).");
            summary.Response<ProjectMembershipStaleResponse>(StatusCodes.Status409Conflict,
                "expectedGoalIds no longer matches the current membership (issueCode projectMembershipStale, "
                + "carrying the current goal ids), or the requested membership contains an occupied active or "
                + "paused goal slot (a ProjectGoalSlotConflictResponse).");
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
            var expectedGoalIds = req.ExpectedGoalIds.Select(GoalId.From).ToHashSet();

            // Read under the project lock (so a concurrent membership change has either committed and is
            // visible here, or is still waiting for the lock).
            var existingMemberships = await db.ProjectGoals
                .Where(entity => entity.ProjectId == projectId)
                .ToListAsync(ct);

            if (!existingMemberships.Select(entity => entity.GoalId).ToHashSet().SetEquals(expectedGoalIds))
            {
                HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                await HttpContext.Response.WriteAsJsonAsync(new ProjectMembershipStaleResponse(
                    "projectMembershipStale",
                    "The project's goals changed after they were reviewed.",
                    project.Id.Value,
                    existingMemberships.Select(entity => entity.GoalId.Value).ToList()), ct);
                return;
            }

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
                    HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await HttpContext.Response.WriteAsJsonAsync(new ProjectLastMembershipResponse(
                        "lastProjectMembership",
                        "Cannot remove a goal from its only remaining project.",
                        orphaned.Select(id => id.Value).ToList()), ct);
                    return;
                }

                db.ProjectGoals.RemoveRange(toRemove);
            }

            // Membership never touches the global order: every member keeps its position.
            var existingByGoalId = existingMemberships.ToDictionary(entity => entity.GoalId);
            var goalsById = ownedGoals.ToDictionary(goal => goal.Id);
            foreach (var goalId in requestedGoalIds)
            {
                if (!existingByGoalId.ContainsKey(goalId))
                {
                    db.ProjectGoals.Add(ProjectGoalPlanningService.CreateMembership(
                        project, goalsById[goalId], DateTimeOffset.UtcNow));
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

            if (transaction is not null)
                await transaction.CommitAsync(ct);

            var updated = await db.ProjectGoals
                .AsNoTracking()
                .Where(entity => entity.ProjectId == projectId)
                .Join(db.Goals, entry => entry.GoalId, goal => goal.Id, (entry, goal) => goal)
                .OrderBy(goal => goal.GlobalPriority == null)
                .ThenBy(goal => goal.GlobalPriority)
                .ThenBy(goal => goal.CreatedAt)
                .ThenBy(goal => goal.Id)
                .Select(goal => new ProjectGoalEntryResponse(goal.Id.Value, goal.GlobalPriority))
                .ToListAsync(ct);

            await Send.OkAsync(new ProjectGoalsResponse(updated), ct);
        }, ct);
    }
}

/// <param name="Goals">The complete desired membership (goal ids only).</param>
/// <param name="ExpectedGoalIds">The complete membership the client reviewed; compared with the current
/// membership under the project lock.</param>
public sealed record UpdateProjectGoalsRequest(List<ProjectGoalEntryRequest> Goals, List<Guid> ExpectedGoalIds);

public sealed class UpdateProjectGoalsValidator : Validator<UpdateProjectGoalsRequest>
{
    public UpdateProjectGoalsValidator()
    {
        RuleFor(request => request.Goals).NotNull();
        RuleFor(request => request.ExpectedGoalIds).NotNull();
    }
}

/// <summary>409 body for a stale <c>expectedGoalIds</c>: the project's current goal ids.</summary>
public sealed record ProjectMembershipStaleResponse(
    string IssueCode, string Message, Guid ProjectId, List<Guid> CurrentGoalIds);

/// <summary>400 body when a removal would leave goals in no project: the goals blocking the save.</summary>
public sealed record ProjectLastMembershipResponse(string IssueCode, string Message, List<Guid> BlockedGoalIds);

/// <summary>No <c>Priority</c> field (see the endpoint's class doc for why) — removed rather than kept
/// and ignored, since a field with no legitimate use is worse than no field.</summary>
public sealed record ProjectGoalEntryRequest(Guid GoalId);

public sealed record ProjectGoalsResponse(List<ProjectGoalEntryResponse> Goals);

/// <summary>A member goal with its account-wide position (null once completed/archived).</summary>
public sealed record ProjectGoalEntryResponse(Guid GoalId, int? GlobalPriority);
