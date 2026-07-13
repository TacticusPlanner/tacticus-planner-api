using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>Lists the authenticated user's goals, excluding soft-deleted ones.</summary>
public sealed class ListGoalsEndpoint : EndpointWithoutRequest<ListGoalsResponse>
{
    public override void Configure()
    {
        Get("me/goals");
        Summary(summary =>
        {
            summary.Summary = "Lists the authenticated user's goals.";
            summary.Description = "Excludes soft-deleted goals. Does not include the config/milestones/"
                + "snapshot/events detail — use GET me/goals/{id} for a single goal's full detail.";
            summary.Response<ListGoalsResponse>(StatusCodes.Status200OK, "The caller's goals.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated account/profile has not been provisioned.");
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

        var db = Resolve<PlannerDbContext>();

        var goals = await db.Goals
            .AsNoTracking()
            .Where(entity => entity.ProfileId == profileId && entity.Status != GoalStatus.Deleted)
            .OrderByDescending(entity => entity.CreatedAt)
            .ToListAsync(ct);

        await Send.OkAsync(new ListGoalsResponse(goals.Select(GoalProjection.BuildSummary).ToList()), ct);
    }
}

public sealed record ListGoalsResponse(List<GoalSummaryResponse> Goals);
