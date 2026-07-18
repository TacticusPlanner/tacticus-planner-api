using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

public sealed class GetGoalEndpoint : EndpointWithoutRequest<GoalDetailResponse, GoalMapper>
{
    public override void Configure()
    {
        Get("me/goals/{goalId}");
        Summary(summary =>
        {
            summary.Summary = "Gets a single goal's full detail, including config, milestones, snapshot, and events.";
            summary.Response<GoalDetailResponse>(StatusCodes.Status200OK, "The goal.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No matching goal owned by the caller.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var goalId = Route<Guid>("goalId");
        var db = Resolve<PlannerDbContext>();

        var goal = await db.Goals.Owned(profileId)
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == GoalId.From(goalId), ct);

        if (goal is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var projectIds = await db.ProjectIdsAsync(goal.Id, ct);
        await Send.OkAsync(Map.ToDetail(goal, projectIds), ct);
    }
}
