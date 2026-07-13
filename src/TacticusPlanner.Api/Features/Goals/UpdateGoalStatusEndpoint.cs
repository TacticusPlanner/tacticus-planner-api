using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>Transitions a goal's lifecycle status (pause/resume/complete/archive) and appends a lifecycle
/// event (plan §10). Deletion goes through <see cref="DeleteGoalEndpoint"/>, not this endpoint.</summary>
public sealed class UpdateGoalStatusEndpoint : Endpoint<UpdateGoalStatusRequest, GoalDetailResponse>
{
    private static readonly HashSet<GoalStatus> AllowedTargets =
    [
        GoalStatus.Active,
        GoalStatus.Paused,
        GoalStatus.Completed,
        GoalStatus.Archived,
    ];

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

        if (!Enum.TryParse<GoalStatus>(req.Status, ignoreCase: true, out var targetStatus)
            || !AllowedTargets.Contains(targetStatus))
        {
            AddError(request => request.Status, "Unknown or unsupported target status.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var goalId = Route<Guid>("goalId");
        var db = Resolve<PlannerDbContext>();

        var goal = await db.Goals.FirstOrDefaultAsync(
            entity => entity.Id == GoalId.From(goalId)
                && entity.ProfileId == profileId
                && entity.Status != GoalStatus.Deleted,
            ct
        );

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

        await Send.OkAsync(GoalProjection.BuildDetail(goal), ct);
    }

    private static string EventTypeFor(GoalStatus status) => status switch
    {
        GoalStatus.Active => "resumed",
        GoalStatus.Paused => "paused",
        GoalStatus.Completed => "completed",
        GoalStatus.Archived => "archived",
        _ => status.ToString().ToLowerInvariant(),
    };
}

public sealed record UpdateGoalStatusRequest(string Status);
