using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>Lists the authenticated user's goals in full detail (<c>goal-collection-read</c>): each entry is
/// exactly what <c>GET me/goals/{goalId}</c> returns, so a client never reads per goal. By default returns
/// everything except archived goals; <c>?archived=true</c> lists only the archived ones — the same selector the
/// projects list applies to its <c>goalIds</c>.</summary>
public sealed class ListGoalsEndpoint : EndpointWithoutRequest<ListGoalsResponse, GoalMapper>
{
    public override void Configure()
    {
        Get("me/goals");
        Summary(summary =>
        {
            summary.Summary = "Lists the authenticated user's goals.";
            summary.Description = "Excludes archived goals by default; pass ?archived=true to list only "
                + "archived goals. Each entry is the full goal detail (config, snapshot, projectIds, revision, "
                + "globalPriority), identical to GET me/goals/{goalId}.";
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
            await this.SendNotFoundProblemAsync(ct);
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
            // Memberships in the same statement, never one read per goal.
            .Select(entity => new
            {
                Goal = entity,
                ProjectIds = db.ProjectGoals.Where(entry => entry.GoalId == entity.Id).Select(entry => entry.ProjectId).ToList(),
            })
            .ToListAsync(ct);

        await Send.OkAsync(
            new ListGoalsResponse(
                goals.Select(row => Map.ToDetail(row.Goal, row.ProjectIds.Select(id => id.Value).ToList())).ToList(),
                revision),
            ct);
    }
}

/// <summary>In-flight goals first in account-wide order (each with its <c>GlobalPriority</c>), then the rest
/// newest first. <c>OrderRevision</c> is the token the reorder operations expect.</summary>
public sealed record ListGoalsResponse(List<GoalDetailResponse> Goals, long OrderRevision);
