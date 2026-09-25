using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>Transitions a goal's lifecycle status (pause/resume/complete/archive, including un-archiving
/// back to active) and appends a lifecycle event (plan §10). Deletion goes through
/// <see cref="DeleteGoalEndpoint"/>, not this endpoint.</summary>
public sealed class UpdateGoalStatusEndpoint : Endpoint<UpdateGoalStatusRequest, GoalDetailResponse, GoalMapper>
{
    public override void Configure()
    {
        Post("me/goals/{goalId}/status");
        Summary(summary =>
        {
            summary.Summary = "Transitions a goal's status (active/paused/completed/archived).";
            summary.Response<GoalDetailResponse>(StatusCodes.Status200OK, "The updated goal.");
            summary.Response(StatusCodes.Status400BadRequest, "Unknown or unsupported target status.");
            summary.Response<ProjectGoalSlotConflictResponse>(StatusCodes.Status409Conflict,
                "A project already contains another active or paused goal in the requested slot.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No matching goal owned by the caller.");
        });
    }

    public override async Task HandleAsync(UpdateGoalStatusRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!Enum.TryParse<GoalStatus>(req.Status, ignoreCase: true, out var targetStatus)
            || !Enum.IsDefined(targetStatus)
            || int.TryParse(req.Status, out _))
        {
            AddError(request => request.Status, "Unknown or unsupported target status.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var goalId = Route<Guid>("goalId");
        var db = Resolve<PlannerDbContext>();

        // Scoped to the caller's profile by PlannerDbContext's global query filter (see
        // PlannerDbContext.ApplyProfileQueryFilters) — no manual ProfileId filtering needed here.
        var goal = await db.Goals.FirstOrDefaultAsync(entity => entity.Id == GoalId.From(goalId), ct);

        if (goal is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var planning = Resolve<ProjectGoalPlanningService>();
        var lockedProjectIds = await db.ProjectGoals
            .Where(entry => entry.GoalId == goal.Id)
            .Select(entry => entry.ProjectId)
            .ToListAsync(ct);

        // A membership can be added to another project between this pre-lock read and the lock actually
        // being acquired below. If the reloaded membership set under the lock turns out to reach outside
        // what was locked, restart with the expanded set rather than let SyncOccupancy/NormalizeAsync
        // mutate an unlocked project's rows (ProjectGoalPlanningService's isolation-level invariant: every
        // project a decision touches must be locked first).
        while (true)
        {
            List<ProjectId>? restartWithProjectIds = null;
            await planning.ExecuteLockedMutationAsync(lockedProjectIds, async transaction =>
            {
                // goal was loaded before the lock; under READ COMMITTED (see ProjectGoalPlanningService's
                // isolation-level invariant) a concurrent status change that committed while this request
                // was waiting for the lock would otherwise be invisible here, letting a stale goal.Status
                // skip the slot-conflict check below for a transition that actually needs it.
                await db.Entry(goal).ReloadAsync(ct);
                var membershipProjectIds = await db.ProjectGoals
                    .Where(entry => entry.GoalId == goal.Id)
                    .Select(entry => entry.ProjectId)
                    .ToListAsync(ct);

                if (membershipProjectIds.Except(lockedProjectIds).Any())
                {
                    restartWithProjectIds = membershipProjectIds.Union(lockedProjectIds).ToList();
                    return;
                }

                if (goal.Status != targetStatus)
                {
                    // At most one Active/Paused goal per (entity, goal type) — mirrors CreateGoalEndpoint's
                    // check. Only entering the slot (targeting Active/Paused) from outside it needs the
                    // check; pausing an already-active goal (or resuming an already-paused one) never
                    // leaves the goal itself out of the count, so it can't conflict with itself.
                    if (targetStatus is GoalStatus.Active or GoalStatus.Paused
                        && await planning.FindConflictAsync(
                            membershipProjectIds, goal.EntityType, goal.EntityId, goal.GoalType,
                            RankTargetKey.For(goal.GoalType, goal.Config), goal.Id, ct) is { } conflict)
                    {
                        HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                        await HttpContext.Response.WriteAsJsonAsync(conflict, ct);
                        return;
                    }

                    goal.Status = targetStatus;
                    goal.Events.Add(new GoalEvent { At = DateTimeOffset.UtcNow, Type = EventTypeFor(targetStatus) });
                    await planning.SyncOccupancyAsync(goal, ct);
                }

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (GoalConflictDetection.IsProjectSlotConflict(ex))
                {
                    var conflict = await planning.FindConflictAfterFailedSaveAsync(
                        transaction,
                        [new ProjectGoalSlotLookup(
                        membershipProjectIds,
                        goal.EntityType,
                        goal.EntityId,
                        goal.GoalType,
                        RankTargetKey.For(goal.GoalType, goal.Config),
                        goal.Id)],
                        ct) ?? throw new InvalidOperationException(
                            "The project slot constraint failed but no conflicting membership was found.", ex);
                    HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                    await HttpContext.Response.WriteAsJsonAsync(conflict, ct);
                    return;
                }

                await planning.NormalizeAsync(membershipProjectIds, ct);
                await db.SaveChangesAsync(ct);
                if (transaction is not null)
                    await transaction.CommitAsync(ct);
                await Send.OkAsync(Map.ToDetail(goal, membershipProjectIds.Select(id => id.Value).ToList()), ct);
            }, ct);

            if (restartWithProjectIds is null)
                break;
            lockedProjectIds = restartWithProjectIds;
        }
    }

    private static GoalEventType EventTypeFor(GoalStatus status) => status switch
    {
        GoalStatus.Active => GoalEventType.Resumed,
        GoalStatus.Paused => GoalEventType.Paused,
        GoalStatus.Completed => GoalEventType.Completed,
        GoalStatus.Archived => GoalEventType.Archived,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported target status."),
    };
}

public sealed record UpdateGoalStatusRequest(string Status);
