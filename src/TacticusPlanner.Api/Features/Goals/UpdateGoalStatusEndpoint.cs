using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Goals;
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
            summary.Response(StatusCodes.Status400BadRequest, "Unknown or unsupported target status, or another "
                + "goal of the same type is already active/paused for this unit.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No matching goal owned by the caller.");
        });
    }

    public override async Task HandleAsync(UpdateGoalStatusRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var targetStatus = Enum.Parse<GoalStatus>(req.Status, ignoreCase: true);

        var goalId = Route<Guid>("goalId");
        var db = Resolve<PlannerDbContext>();

        var goal = await db.Goals.Owned(profileId).FirstOrDefaultAsync(entity => entity.Id == GoalId.From(goalId), ct);

        if (goal is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (goal.Status != targetStatus)
        {
            // At most one Active/Paused goal per (entity, goal type) — mirrors CreateGoalEndpoint's
            // check. Only entering the slot (targeting Active/Paused) from outside it needs the check;
            // pausing an already-active goal (or resuming an already-paused one) never leaves the goal
            // itself out of the count, so it can't conflict with itself.
            if (targetStatus is GoalStatus.Active or GoalStatus.Paused)
            {
                var hasConflictingGoal = await db.Goals.Owned(profileId)
                    .Where(entity => entity.Id != goal.Id
                        && entity.EntityType == goal.EntityType
                        && entity.EntityId == goal.EntityId
                        && entity.GoalType == goal.GoalType
                        && (entity.Status == GoalStatus.Active || entity.Status == GoalStatus.Paused))
                    .AnyAsync(ct);
                if (hasConflictingGoal)
                {
                    AddError(request => request.Status, "An active or paused goal of this type already exists for this unit.");
                    await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                    return;
                }
            }

            goal.Status = targetStatus;
            goal.Events.Add(new GoalEvent { At = DateTimeOffset.UtcNow, Type = EventTypeFor(targetStatus) });
        }

        await db.SaveChangesAsync(ct);

        var projectIds = await db.ProjectIdsAsync(goal.Id, ct);
        await Send.OkAsync(Map.ToDetail(goal, projectIds), ct);
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
