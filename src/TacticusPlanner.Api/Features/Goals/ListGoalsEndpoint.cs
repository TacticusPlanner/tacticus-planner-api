using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>Lists the authenticated user's goals. By default returns everything except archived goals;
/// pass <c>?archived=true</c> to list only the archived ones (the archived tab), matching the two-view
/// split the frontend surfaces rather than mixing both into one list.</summary>
public sealed class ListGoalsEndpoint : EndpointWithoutRequest<ListGoalsResponse, GoalMapper>
{
    public override void Configure()
    {
        Get("me/goals");
        Summary(summary =>
        {
            summary.Summary = "Lists the authenticated user's goals.";
            summary.Description = "Excludes archived goals by default; pass ?archived=true to list only "
                + "archived goals. Does not include the config/milestones/snapshot/events detail — use "
                + "GET me/goals/{id} for a single goal's full detail.";
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

        var archived = Query<bool?>("archived", isRequired: false) ?? false;
        var db = Resolve<PlannerDbContext>();

        var goals = await db.Goals.Owned(profileId)
            .AsNoTracking()
            .Where(entity => archived ? entity.Status == GoalStatus.Archived : entity.Status != GoalStatus.Archived)
            .OrderByDescending(entity => entity.CreatedAt)
            .ToListAsync(ct);

        await Send.OkAsync(new ListGoalsResponse(goals.Select(Map.ToSummary).ToList()), ct);
    }
}

public sealed record ListGoalsResponse(List<GoalSummaryResponse> Goals);
