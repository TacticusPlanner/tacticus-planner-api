using FastEndpoints;
using FluentValidation;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Request-shape rules only — entity/goal type must parse to a supported (non-reserved) value, an
/// entity id is required, and a Machine of War may not be given a Rank goal (MoWs have no rank
/// ladder — plan §16 phase 6). Project ownership (a DB lookup) stays a handler-level check.
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

        // Machines of War have no rank ladder (plan §16 phase 6) — reject the combination before it
        // reaches MilestoneGenerator, which would otherwise stamp character-rank milestones onto it.
        RuleFor(request => request)
            .Must(request => !(IsMow(request.EntityType) && IsRank(request.GoalType)))
            .WithMessage("Machines of War have no rank — use an Ability goal instead.");
    }

    private static bool IsMow(string entityType) =>
        Enum.TryParse<GoalEntityType>(entityType, ignoreCase: true, out var value)
            && value == GoalEntityType.Mow;

    private static bool IsRank(string goalType) =>
        Enum.TryParse<GoalType>(goalType, ignoreCase: true, out var value) && value == GoalType.Rank;
}
