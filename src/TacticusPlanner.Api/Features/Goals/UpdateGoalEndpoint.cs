using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Updates a goal's editable fields only (plan §7). The target end-state in <see cref="Goal.Config"/> and
/// the creation snapshot are immutable — redefining them means creating a replacement goal, not editing
/// this one. Only <c>notes</c> and the farming override are writable here.
/// </summary>
public sealed class UpdateGoalEndpoint : Endpoint<UpdateGoalRequest, GoalDetailResponse>
{
    public override void Configure()
    {
        Put("me/goals/{goalId}");
        Summary(summary =>
        {
            summary.Summary = "Updates a goal's editable fields (notes, farming override).";
            summary.Description = "The goal's target end-state and creation snapshot cannot be changed here "
                + "— create a replacement goal instead.";
            summary.Response<GoalDetailResponse>(StatusCodes.Status200OK, "The updated goal.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No matching goal owned by the caller.");
        });
    }

    public override async Task HandleAsync(UpdateGoalRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
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

        goal.Notes = req.Notes;
        goal.Config.FarmingMode = req.FarmingMode;
        goal.Config.FarmingLocationIds = req.FarmingLocationIds;

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(GoalProjection.BuildDetail(goal), ct);
    }
}

public sealed record UpdateGoalRequest(string? Notes, string? FarmingMode, List<string>? FarmingLocationIds);
