using FastEndpoints;
using FluentValidation;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// The only goal edit route (<c>goal-combined-edit</c>): any combination of the target, details,
/// project-membership and priority sections, applied in that order inside one profile-locked transaction with
/// one <c>SaveChangesAsync</c>. Any non-applied section rolls everything back. The response is written once,
/// after the locked delegate returns (the execution strategy may not replay a delegate that already wrote one).
/// </summary>
public sealed class EditGoalEndpoint : Endpoint<EditGoalRequest, EditGoalResponse, GoalMapper>
{
    public override void Configure()
    {
        Put("me/goals/{goalId}");
        Summary(summary =>
        {
            summary.Summary = "Edits a goal's target, details, project memberships and priority in one atomic request.";
            summary.Description = "Every section is optional and an absent section leaves that aspect unchanged; "
                + "a request with no section is a no-op. Sections apply in the order target, details, projectIds, "
                + "priority; if any fails nothing is saved. target carries its own expectedRevision (the goal "
                + "revision); priority.position is the 1-based position among the account's Active/Paused goals and "
                + "priority.expectedOrderRevision the order revision the client computed it from. The response "
                + "carries the updated goal and the new order (null unless priority was supplied).";
            summary.Response<EditGoalResponse>(StatusCodes.Status200OK, "The updated (or unchanged) goal and, when priority was supplied, the new order.");
            summary.Response(StatusCodes.Status400BadRequest,
                "A section is invalid (the errors name it), e.g. a bad target, unknown project, or a position out of range.");
            summary.Response(StatusCodes.Status409Conflict,
                "goalRevisionStale (current: the goal), goalOrderStale (current: { revision, goalIds }) or "
                + "slotOccupied (current: the goal holding the slot); nothing changed.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No matching goal owned by the caller.");
        });
    }

    public override async Task HandleAsync(EditGoalRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var (result, goal, projectIds, order) = await Resolve<GoalCombinedEditor>().ApplyAsync(
            profileId, GoalId.From(Route<Guid>("goalId")), req, ct);
        switch (result)
        {
            case GoalEditResult.Applied:
                await Send.OkAsync(
                    new EditGoalResponse(
                        Map.ToDetail(goal!, projectIds),
                        order is null ? null : new GoalOrderResponse(order.Revision, order.GoalIds.Select(id => id.Value).ToList())),
                    ct);
                break;
            case GoalEditResult.Invalid invalid:
                ValidationFailures.Add(new(invalid.Field, invalid.Message));
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                break;
            case GoalEditResult.StaleRevision stale:
                await GoalProblems.SendStaleRevisionAsync(this, Map.ToDetail(stale.Current, stale.ProjectIds), ct);
                break;
            case GoalEditResult.SlotConflict slotConflict:
                await GoalProblems.SendSlotConflictAsync(this, slotConflict.Conflict, ct);
                break;
            case GoalEditResult.OrderConflict orderConflict:
                await GoalOrderResponses.SendAsync(this, orderConflict.Order, transaction: null, ct);
                break;
            default:
                await this.SendNotFoundProblemAsync(ct);
                break;
        }
    }
}

/// <param name="Target">The goal <c>expectedRevision</c> and the end target.</param>
/// <param name="Details">Notes, farming locations/strategy and acquisition sources (null notes or farming
/// locations clear them; a null strategy leaves it).</param>
/// <param name="ProjectIds">The complete replacement membership (non-empty when present).</param>
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

/// <param name="Goal">The goal after the edit, with its own <c>revision</c>.</param>
/// <param name="Order">The order after the move (its own <c>revision</c>); null unless the request carried
/// <c>priority</c>.</param>
public sealed record EditGoalResponse(GoalDetailResponse Goal, GoalOrderResponse? Order);

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

/// <param name="ExpectedRevision">The <c>revision</c> of the goal detail the editor started from.</param>
public sealed record UpdateGoalTargetRequest(long ExpectedRevision, GoalTargetEditRequest Target);

/// <summary>Exactly the group matching the goal's kind must be set; every other group must be null.
/// Only end values are editable — the start/baseline stays as stored.</summary>
public sealed record GoalTargetEditRequest(
    RankEndTargetRequest? Rank = null,
    ProgressionEndTargetRequest? Progression = null,
    AbilityEndTargetRequest? Ability = null,
    UpgradeTargetRequest? Upgrade = null
);

public sealed record RankEndTargetRequest(int End, bool EndPointFive, int EndAppliedUpgrades);

public sealed record ProgressionEndTargetRequest(string End);

public sealed record AbilityEndTargetRequest(int ActiveEnd, int PassiveEnd);

public sealed class UpdateGoalTargetValidator : AbstractValidator<UpdateGoalTargetRequest>
{
    public UpdateGoalTargetValidator()
    {
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Target).NotNull();

        // Creation returns a 400 for a missing upgrade list / blank ids; the edit must too, before the
        // handler dereferences them.
        When(request => request.Target?.Upgrade is not null, () =>
        {
            RuleFor(request => request.Target.Upgrade!.Targets)
                .NotNull()
                .WithMessage("An upgrade target needs a list of materials.");
            RuleForEach(request => request.Target.Upgrade!.Targets)
                .Must(target => target is not null && !string.IsNullOrWhiteSpace(target.UpgradeId))
                .When(request => request.Target.Upgrade!.Targets is not null)
                .WithMessage("Every upgrade target needs an upgrade id.");
            RuleFor(request => request.Target.Upgrade!)
                .Must(UpgradeRangeRules.HasWholeRanges)
                .WithMessage(UpgradeRangeRules.ShapeMessage);
        });

        When(request => request.Target?.Progression is not null, () =>
            RuleFor(request => request.Target.Progression!.End)
                .NotEmpty()
                .WithMessage("An Ascension target needs a progression step."));
    }
}

public sealed record UpdateGoalRequest(
    string? Notes,
    List<CampaignBattleId>? FarmingLocationIds,
    string? FarmingStrategy = null,
    List<AcquisitionSourceRequest>? AcquisitionSources = null
);

public sealed class UpdateGoalValidator : AbstractValidator<UpdateGoalRequest>
{
    public UpdateGoalValidator()
    {
        RuleFor(request => request.Notes)
            .MaximumLength(GoalValidation.MaxNotesLength);

        RuleFor(request => request.FarmingStrategy)
            .Must(value => value is null
                || (Enum.TryParse<FarmingStrategy>(value, ignoreCase: true, out var parsed)
                    && Enum.IsDefined(parsed)
                    && !int.TryParse(value, out _)))
            .WithMessage("Unknown farming strategy.");

        RuleFor(request => request.AcquisitionSources)
            .Must(sources => AcquisitionSourceRules.ShapeError(sources) is null)
            .WithMessage(request =>
                AcquisitionSourceRules.ShapeError(request.AcquisitionSources)
                ?? "The acquisition sources configuration is invalid.");
    }
}
