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
            summary.Response(StatusCodes.Status400BadRequest, "Unknown or unsupported target status.");
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
            goal.Status = targetStatus;
            goal.Events.Add(new GoalEvent { At = DateTimeOffset.UtcNow, Type = EventTypeFor(targetStatus) });
        }

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(Map.FromEntity(goal), ct);
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
