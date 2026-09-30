using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Updates a goal's editable fields only (plan §7). The target end-state in <see cref="Goal.Config"/> is
/// changed through <see cref="UpdateGoalTargetEndpoint"/> instead, and the creation snapshot is immutable.
/// Only <c>notes</c> and the farming-location override are writable here. Null semantics differ
/// deliberately by field: a null <see cref="UpdateGoalRequest.Notes"/> or
/// <see cref="UpdateGoalRequest.FarmingLocationIds"/> clears that field (there is no other way to clear
/// it), while a null <see cref="UpdateGoalRequest.FarmingStrategy"/> means "leave unchanged" (it has a
/// server-assigned default from creation, so there's nothing meaningful to clear it to).
/// </summary>
public sealed class UpdateGoalEndpoint : Endpoint<UpdateGoalRequest, GoalDetailResponse, GoalMapper>
{
    public override void Configure()
    {
        Put("me/goals/{goalId}");
        Summary(summary =>
        {
            summary.Summary = "Updates a goal's editable fields (notes, farming-location override).";
            summary.Description = "The goal's target end-state cannot be changed here — use "
                + "PUT /me/goals/{goalId}/target — and its creation snapshot cannot be changed at all.";
            summary.Response<GoalDetailResponse>(StatusCodes.Status200OK, "The updated goal.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No matching goal owned by the caller.");
        });
    }

    public override async Task HandleAsync(UpdateGoalRequest req, CancellationToken ct)
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

        if (Resolve<GoalDetailsEditor>().Apply(goal, req) is GoalEditResult.Invalid invalid)
        {
            ValidationFailures.Add(new(invalid.Field, invalid.Message));
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        await db.SaveChangesAsync(ct);

        var projectIds = await db.ProjectIdsAsync(goal.Id, ct);
        await Send.OkAsync(Map.ToDetail(goal, projectIds), ct);
    }
}

public sealed record UpdateGoalRequest(
    string? Notes,
    List<CampaignBattleId>? FarmingLocationIds,
    string? FarmingStrategy = null,
    List<AcquisitionSourceRequest>? AcquisitionSources = null
);

public sealed class UpdateGoalValidator : Validator<UpdateGoalRequest>
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
