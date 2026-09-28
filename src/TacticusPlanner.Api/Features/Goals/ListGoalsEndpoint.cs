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
        if (state.ProfileId is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var archived = Query<bool?>("archived", isRequired: false) ?? false;
        var db = Resolve<PlannerDbContext>();

        // Scoped to the caller's profile by PlannerDbContext's global query filter.
        // Revision first: if a reorder commits between the two reads the client gets a newer order with an
        // older revision, which the reorder operations reject, never the other way round.
        var revision = (await db.Profiles.AsNoTracking().FirstAsync(ct)).GoalOrderRevision;
        var goals = await db.Goals
            .AsNoTracking()
            .Where(entity => archived
                ? entity.Status == GoalStatus.Archived
                : entity.Status != GoalStatus.Archived)
            .OrderBy(entity => entity.GlobalPriority == null)
            .ThenBy(entity => entity.GlobalPriority)
            .ThenByDescending(entity => entity.CreatedAt)
            .ThenBy(entity => entity.Id)
            .ToListAsync(ct);

        await Send.OkAsync(new ListGoalsResponse(goals.Select(Map.ToSummary).ToList(), revision), ct);
    }
}

/// <summary>In-flight goals first in account-wide order (each with its <c>GlobalPriority</c>), then the rest
/// newest first. <c>OrderRevision</c> is the token the reorder operations expect.</summary>
public sealed record ListGoalsResponse(List<GoalSummaryResponse> Goals, long OrderRevision = 0);
