using FastEndpoints;
using FluentValidation;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Reorders the account's complete in-flight goal set (spec: <c>global-goal-priority</c>). The caller
/// submits every Active/Paused goal id in the desired order plus the order revision it loaded; a stale
/// revision or set is rejected atomically with the current order so the client can review and retry.
/// Changes priority only — never status, membership, targets or dependencies, and dependency order is
/// deliberately not enforced.
/// </summary>
public sealed class UpdateGoalOrderEndpoint : Endpoint<UpdateGoalOrderRequest, GoalOrderResponse>
{
    public override void Configure()
    {
        Put("goals/order");
        Summary(summary =>
        {
            summary.Summary = "Reorders every in-flight goal in the account-wide order.";
            summary.Description = "GoalIds must be the exact, duplicate-free set of the caller's Active and "
                + "Paused goals, ExpectedRevision the revision last read. Any ordering is accepted, including "
                + "one that places a goal ahead of a DependsOn prerequisite it hasn't reached.";
            summary.Response<GoalOrderResponse>(StatusCodes.Status200OK, "The new revision and canonical order.");
            summary.Response(StatusCodes.Status409Conflict,
                "The goal set or revision is stale, or the request repeats a goal; nothing changed.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated account/profile has not been provisioned.");
        });
    }

    public override async Task HandleAsync(UpdateGoalOrderRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var planning = Resolve<ProjectGoalPlanningService>();
        var order = Resolve<GoalOrderService>();
        await planning.ExecuteLockedMutationAsync(async transaction =>
        {
            var result = await order.ReorderAsync(req.GoalIds.Select(GoalId.From).ToList(), req.ExpectedRevision, ct);
            await GoalOrderResponses.SendAsync(this, result, transaction, ct);
        }, ct);
    }
}

public sealed record UpdateGoalOrderRequest(List<Guid> GoalIds, long ExpectedRevision);

public sealed class UpdateGoalOrderValidator : Validator<UpdateGoalOrderRequest>
{
    public UpdateGoalOrderValidator()
    {
        RuleFor(request => request.GoalIds).NotNull();
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0);
    }
}

public sealed record GoalOrderResponse(long Revision, List<Guid> GoalIds);

internal static class GoalOrderResponses
{
    /// <summary>Sends 200 with the new order (committing) or the 409 problem naming the rejection, with the order
    /// as it stands under <c>current</c> (nothing changed).</summary>
    public static async Task SendAsync(
        IEndpoint endpoint,
        GoalOrderResult result,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken ct)
    {
        var order = new GoalOrderResponse(result.Order.Revision, result.Order.GoalIds.Select(id => id.Value).ToList());
        if (result.Outcome == GoalOrderOutcome.Ok)
        {
            // Error responses may be sent outside a mutation; successful writes always supply a transaction.
            if (transaction is not null)
                await transaction.CommitAsync(ct);
            await endpoint.HttpContext.Response.WriteAsJsonAsync(order, ct);
            return;
        }

        var (code, message) = result.Outcome switch
        {
            GoalOrderOutcome.StaleRevision => (GoalProblems.GoalOrderStale, "The goal order changed since it was loaded."),
            GoalOrderOutcome.DuplicateGoal => ("goalOrderDuplicate", "A goal appears more than once in the request."),
            GoalOrderOutcome.SameGoal => ("goalOrderSameGoal", "A goal cannot be moved onto itself."),
            _ => ("goalOrderSetMismatch", "The goals do not match the account's current in-flight goals."),
        };
        await endpoint.SendConflictAsync(code, message, order, ct);
    }
}
