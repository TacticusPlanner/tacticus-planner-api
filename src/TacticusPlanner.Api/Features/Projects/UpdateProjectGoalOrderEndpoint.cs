using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>
/// Moves one in-flight goal within a project's projection of the global order (spec:
/// <c>global-goal-priority</c>). A project has no order of its own: the moved goal takes the global
/// position of the project goal it displaces, goals in between (members or not) shift one place toward
/// the vacated slot, and every other relative order is unchanged. With global A,B,C,D,E and a project
/// holding A,C,E, moving E onto C gives A,B,E,C,D.
/// </summary>
public sealed class UpdateProjectGoalOrderEndpoint : Endpoint<UpdateProjectGoalOrderRequest, GoalOrderResponse>
{
    public override void Configure()
    {
        Put("me/projects/{projectId}/goal-order");
        Summary(summary =>
        {
            summary.Summary = "Moves a project goal onto the global position of another project goal.";
            summary.Description = "Both goals must be in-flight members of the project. The move writes "
                + "through to the account-wide order; it never changes membership, status or dependencies. "
                + "ExpectedRevision is the order revision last read.";
            summary.Response<GoalOrderResponse>(StatusCodes.Status200OK, "The new revision and canonical order.");
            summary.Response<GoalOrderConflictResponse>(StatusCodes.Status409Conflict,
                "The revision is stale, a goal is not an in-flight member of the project, or both goals are the same.");
            summary.Response(StatusCodes.Status404NotFound);
        });
    }

    public override async Task HandleAsync(UpdateProjectGoalOrderRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var projectId = ProjectId.From(Route<Guid>("projectId"));
        if (!await db.Projects.AnyAsync(project => project.Id == projectId, ct))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var goalId = GoalId.From(req.GoalId);
        var displacedGoalId = GoalId.From(req.DisplacedGoalId);
        var planning = Resolve<ProjectGoalPlanningService>();
        var order = Resolve<GoalOrderService>();
        await planning.ExecuteLockedMutationAsync([projectId], async transaction =>
        {
            var inFlightMembers = await db.ProjectGoals.AsNoTracking()
                .Where(entry => entry.ProjectId == projectId && entry.OccupiesInFlightSlot
                    && (entry.GoalId == goalId || entry.GoalId == displacedGoalId))
                .Select(entry => entry.GoalId)
                .ToListAsync(ct);
            var result = inFlightMembers.Distinct().Count() == (goalId == displacedGoalId ? 1 : 2)
                ? await order.MoveAsync(goalId, displacedGoalId, req.ExpectedRevision, ct)
                // Not both in-flight members: same conflict shape as a stale set, current order attached.
                : new GoalOrderResult(GoalOrderOutcome.SetMismatch, await order.ReadAsync(ct));
            await GoalOrderResponses.SendAsync(this, result, transaction, ct);
        }, ct);
    }
}

public sealed record UpdateProjectGoalOrderRequest(Guid GoalId, Guid DisplacedGoalId, long ExpectedRevision);

public sealed class UpdateProjectGoalOrderValidator : Validator<UpdateProjectGoalOrderRequest>
{
    public UpdateProjectGoalOrderValidator()
    {
        RuleFor(request => request.GoalId).NotEmpty();
        RuleFor(request => request.DisplacedGoalId).NotEmpty();
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0);
    }
}
