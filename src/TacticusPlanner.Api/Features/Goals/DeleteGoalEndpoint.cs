using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>Permanently deletes a goal — a hard delete, not a status change. Cascades to its
/// <c>project_goals</c> memberships (see <c>ProjectGoalConfiguration</c>'s cascade delete on
/// <c>GoalId</c>). Archived goals are not deleted by this; they stay retrievable via the archived tab
/// (<c>GET me/goals?archived=true</c>) until explicitly deleted here.</summary>
public sealed class DeleteGoalEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("me/goals/{goalId}");
        Summary(summary =>
        {
            summary.Summary = "Permanently deletes a goal.";
            summary.Response(StatusCodes.Status204NoContent, "The goal was deleted.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No matching goal owned by the caller.");
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

        var goalId = Route<Guid>("goalId");
        var db = Resolve<PlannerDbContext>();

        // Scoped to the caller's profile by PlannerDbContext's global query filter.
        var goal = await db.Goals.FirstOrDefaultAsync(entity => entity.Id == GoalId.From(goalId), ct);

        if (goal is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        db.Goals.Remove(goal);

        await db.SaveChangesAsync(ct);

        await Send.NoContentAsync(ct);
    }
}
