using FastEndpoints;
using FluentValidation;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// All-or-nothing edit of one goal (<c>goal-combined-edit</c>): any combination of the target, details,
/// project-membership and priority sections, applied in that order inside one locked transaction with one
/// <c>SaveChangesAsync</c>. Each section runs the same core as its own endpoint
/// (<see cref="GoalTargetEditor"/>, <see cref="GoalDetailsEditor"/>, <see cref="GoalMembershipEditor"/>,
/// <see cref="GoalOrderService.MoveToPositionAsync"/>), so validation and 400/409 bodies are identical.
/// Any non-applied result rolls everything back. The response is written once, after the locked delegate
/// returns (the execution strategy may not replay a delegate that already wrote one).
/// </summary>
public sealed class EditGoalEndpoint : Endpoint<EditGoalRequest, EditGoalResponse, GoalMapper>
{
    public override void Configure()
    {
        Put("me/goals/{goalId}/edit");
        Summary(summary =>
        {
            summary.Summary = "Edits a goal's target, details, project memberships and priority in one atomic request.";
            summary.Description = "Every section is optional and an absent section leaves that aspect unchanged. "
                + "Sections apply in the order target, details, projectIds, priority; if any fails nothing is "
                + "saved. target carries its own expectedRevision (the goal revision); priority.position is the "
                + "1-based position among the account's Active/Paused goals and priority.expectedOrderRevision "
                + "the order revision the client computed it from. The response carries the updated goal and, "
                + "when priority was supplied, the new order.";
            summary.Response<EditGoalResponse>(StatusCodes.Status200OK, "The updated (or unchanged) goal, plus the new order when priority was supplied.");
            summary.Response(StatusCodes.Status400BadRequest,
                "A section is invalid (the error names it), e.g. a bad target, unknown project, or a position out of range.");
            summary.Response<GoalRevisionConflictResponse>(StatusCodes.Status409Conflict,
                "target.expectedRevision is stale (goalRevisionStale, carrying the current goal), the order "
                + "revision is stale (a GoalOrderConflictResponse), or a project slot is occupied "
                + "(a ProjectGoalSlotConflictResponse); nothing changed.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No matching goal owned by the caller.");
        });
    }

    public override async Task HandleAsync(EditGoalRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var outcome = await Resolve<GoalCombinedEditor>().ApplyAsync(
            profileId, GoalId.From(Route<Guid>("goalId")), req, ct);
        await SendResultAsync(outcome, ct);
    }

    private async Task SendInvalidAsync(string field, string message, CancellationToken ct)
    {
        ValidationFailures.Add(new(field, message));
        await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
    }

    private async Task SendResultAsync(GoalEditOutcome outcome, CancellationToken ct)
    {
        var (result, goal, projectIds, order) = outcome;
        switch (result)
        {
            case GoalEditResult.Applied:
                await Send.OkAsync(
                    new EditGoalResponse(
                        Map.ToDetail(goal!, projectIds.Select(id => id.Value).ToList()),
                        order is null ? null : new GoalOrderResponse(order.Revision, order.GoalIds.Select(id => id.Value).ToList())),
                    ct);
                break;
            case GoalEditResult.Invalid invalid:
                await SendInvalidAsync(invalid.Field, invalid.Message, ct);
                break;
            case GoalEditResult.StaleRevision stale:
                HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                await HttpContext.Response.WriteAsJsonAsync(
                    new GoalRevisionConflictResponse(
                        "goalRevisionStale",
                        "The goal changed since it was loaded. Review the current goal and try again.",
                        Map.ToDetail(stale.Current, stale.ProjectIds.Select(id => id.Value).ToList())),
                    ct);
                break;
            case GoalEditResult.SlotConflict slotConflict:
                HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                await HttpContext.Response.WriteAsJsonAsync(slotConflict.Body, ct);
                break;
            case GoalEditResult.OrderConflict orderConflict:
                await GoalOrderResponses.SendAsync(this, orderConflict.Order, transaction: null, ct);
                break;
            default:
                await Send.NotFoundAsync(ct);
                break;
        }
    }
}

/// <param name="Target">Same payload as <c>PUT me/goals/{goalId}/target</c>: the goal <c>expectedRevision</c> and the end target.</param>
/// <param name="Details">Same payload and null semantics as <c>PUT me/goals/{goalId}</c>.</param>
/// <param name="ProjectIds">Same replacement list as <c>PUT me/goals/{goalId}/projects</c> (non-empty when present).</param>
/// <param name="Priority">Move the goal to a position in the account-wide order.</param>
public sealed record EditGoalRequest(
    UpdateGoalTargetRequest? Target = null,
    UpdateGoalRequest? Details = null,
    List<Guid>? ProjectIds = null,
    EditGoalPriorityRequest? Priority = null
);

/// <param name="Position">1-based position among the account's Active/Paused goals.</param>
/// <param name="ExpectedOrderRevision">The order revision the position was computed from.</param>
public sealed record EditGoalPriorityRequest(int Position, long ExpectedOrderRevision);

/// <param name="Order">Present only when the request carried <c>priority</c>.</param>
public sealed record EditGoalResponse(GoalDetailResponse Goal, GoalOrderResponse? Order = null);

public sealed class EditGoalValidator : Validator<EditGoalRequest>
{
    public EditGoalValidator()
    {
        RuleFor(request => request.Target!).SetValidator(new UpdateGoalTargetValidator()).When(request => request.Target is not null);
        RuleFor(request => request.Details!).SetValidator(new UpdateGoalValidator()).When(request => request.Details is not null);
        RuleFor(request => request.ProjectIds)
            .NotEmpty()
            .WithMessage("A goal must belong to at least one project.")
            .When(request => request.ProjectIds is not null);
        RuleFor(request => request.Priority!.ExpectedOrderRevision)
            .GreaterThanOrEqualTo(0)
            .When(request => request.Priority is not null);
    }
}
