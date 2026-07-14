using FastEndpoints;
using FluentValidation;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Request-shape rules only — entity/goal type must parse to a supported (non-reserved) value, and an
/// entity id is required. Project ownership (a DB lookup) stays a handler-level check.
/// </summary>
public sealed class CreateGoalValidator : Validator<CreateGoalRequest>
{
    public CreateGoalValidator()
    {
        RuleFor(request => request.EntityType)
            .Must(value => Enum.TryParse<GoalEntityType>(value, ignoreCase: true, out var entityType)
                && entityType != GoalEntityType.Upgrade)
            .WithMessage("Unknown or not-yet-supported entity type.");

        RuleFor(request => request.GoalType)
            .Must(value => Enum.TryParse<GoalType>(value, ignoreCase: true, out var goalType)
                && goalType != GoalType.Material)
            .WithMessage("Unknown or not-yet-supported goal type.");

        RuleFor(request => request.EntityId)
            .NotEmpty()
            .WithMessage("An entity id is required.");
    }
}
