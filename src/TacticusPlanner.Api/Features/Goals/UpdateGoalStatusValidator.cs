using FastEndpoints;
using FluentValidation;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>Request-shape rule: the target status must parse to one of the caller-settable values.</summary>
public sealed class UpdateGoalStatusValidator : Validator<UpdateGoalStatusRequest>
{
    private static readonly HashSet<GoalStatus> AllowedTargets =
    [
        GoalStatus.Active,
        GoalStatus.Paused,
        GoalStatus.Completed,
        GoalStatus.Archived,
    ];

    public UpdateGoalStatusValidator()
    {
        RuleFor(request => request.Status)
            .Must(value => Enum.TryParse<GoalStatus>(value, ignoreCase: true, out var status)
                && Enum.IsDefined(status)
                && !int.TryParse(value, out _)
                && AllowedTargets.Contains(status))
            .WithMessage("Unknown or unsupported target status.");
    }
}
