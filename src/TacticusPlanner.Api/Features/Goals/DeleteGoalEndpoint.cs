using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>Deletes a goal — soft by default (plan §12), so history/Insights stay coherent; pass
/// <c>?purge=true</c> to permanently remove the row and its project memberships.</summary>
public sealed class DeleteGoalEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("me/goals/{goalId}");
        Summary(summary =>
        {
            summary.Summary = "Deletes a goal.";
            summary.Description = "Soft-deletes by default (status becomes 'deleted', row is retained for "
                + "history). Pass ?purge=true to permanently remove the goal and its project memberships.";
            summary.Response(StatusCodes.Status204NoContent, "The goal was deleted.");
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
        var purge = Query<bool?>("purge", isRequired: false) ?? false;
        var db = Resolve<PlannerDbContext>();

        var goal = await db.Goals.FirstOrDefaultAsync(
            entity => entity.Id == GoalId.From(goalId) && entity.ProfileId == profileId,
            ct
        );

        if (goal is null || (!purge && goal.Status == GoalStatus.Deleted))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (purge)
        {
            db.Goals.Remove(goal);
        }
        else
        {
            goal.Status = GoalStatus.Deleted;
            goal.Events.Add(new GoalEvent { At = DateTimeOffset.UtcNow, Type = "deleted" });
        }

        await db.SaveChangesAsync(ct);

        await Send.NoContentAsync(ct);
    }
}
