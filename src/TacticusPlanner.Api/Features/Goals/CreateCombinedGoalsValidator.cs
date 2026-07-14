using FastEndpoints;
using FluentValidation;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Request-shape rules only — entity type must parse to a supported (non-reserved) value (mirrors
/// <see cref="CreateGoalValidator"/>), each spec's goal type must parse to a supported value, the list
/// must be non-empty and reasonably small (Unlock + Ascension + Rank + Ability is the largest realistic
/// combined request), and every <see cref="CombinedGoalSpec.DependsOnIndex"/> must reference a strictly
/// earlier spec in the same request (no forward or self references). Project ownership stays a
/// handler-level check.
/// </summary>
public sealed class CreateCombinedGoalsValidator : Validator<CreateCombinedGoalsRequest>
{
    private const int MaxGoals = 4;

    public CreateCombinedGoalsValidator()
    {
        RuleFor(request => request.EntityType)
            .Must(value => Enum.TryParse<GoalEntityType>(value, ignoreCase: true, out var entityType)
                && entityType != GoalEntityType.Upgrade)
            .WithMessage("Unknown or not-yet-supported entity type.");

        RuleFor(request => request.EntityId)
            .NotEmpty()
            .WithMessage("An entity id is required.");

        RuleFor(request => request.Goals)
            .NotEmpty()
            .WithMessage("At least one goal is required.")
            .Must(goals => goals.Count <= MaxGoals)
            .WithMessage($"A combined request can create at most {MaxGoals} goals.");

        RuleForEach(request => request.Goals)
            .ChildRules(goal => goal.RuleFor(spec => spec.GoalType)
                .Must(value => Enum.TryParse<GoalType>(value, ignoreCase: true, out var goalType)
                    && goalType != GoalType.Material)
                .WithMessage("Unknown or not-yet-supported goal type."));

        RuleFor(request => request.Goals)
            .Custom((goals, context) =>
            {
                for (var i = 0; i < goals.Count; i++)
                {
                    foreach (var dependsOnIndex in goals[i].DependsOnIndex)
                    {
                        if (dependsOnIndex < 0 || dependsOnIndex >= i)
                        {
                            context.AddFailure(
                                $"Goals[{i}].DependsOnIndex",
                                "Each DependsOnIndex must reference an earlier goal in the same request."
                            );
                        }
                    }
                }
            });
    }
}
