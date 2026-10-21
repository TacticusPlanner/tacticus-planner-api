using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>Permanently deletes a goal — a hard delete, not a status change. Cascades to its
/// <c>project_goals</c> memberships, and in the same transaction removes the goal from every dependent's
/// <c>DependsOn</c>. Archived goals are not deleted by this; they stay retrievable via the archived tab
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
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var goalId = Route<Guid>("goalId");
        var db = Resolve<PlannerDbContext>();

        // Under the profile lock: a deleted in-flight goal leaves the global order, which then compacts.
        var order = Resolve<GoalOrderService>();
        await Resolve<ProjectGoalPlanningService>().ExecuteLockedMutationAsync(async transaction =>
        {
            // Scoped to the caller's profile by PlannerDbContext's global query filter. The goal and its dependents
            // (no foreign key guards DependsOn, a uuid[]) in one read.
            var related = await db.Goals
                .Where(entity => entity.Id == GoalId.From(goalId) || entity.DependsOn.Contains(goalId))
                .ToListAsync(ct);
            var goal = related.FirstOrDefault(entity => entity.Id == GoalId.From(goalId));
            if (goal is null)
            {
                await this.SendNotFoundProblemAsync(ct);
                return;
            }

            order.Release(goal);
            db.Goals.Remove(goal);
            foreach (var dependent in related.Where(entity => entity != goal))
                dependent.DependsOn = dependent.DependsOn.Where(id => id != goalId).ToList();
            await order.CompleteAsync(ct);
            await transaction.CommitAsync(ct);

            await Send.NoContentAsync(ct);
        }, ct);
    }
}
