using FastEndpoints;
using FluentValidation;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Request-shape rules only — entity type must parse to a supported value (mirrors
/// <see cref="CreateGoalValidator"/>), each spec's goal type must parse to a supported value, the list
/// must be non-empty and reasonably small (Unlock + Ascension + Rank + Ability is the largest realistic
/// combined request), every <see cref="CombinedGoalSpec.DependsOnIndex"/> must reference a strictly
/// earlier spec in the same request (no forward or self references), and a Machine of War request may
/// not include a Rank goal (MoWs have no rank ladder — plan §16 phase 6). Project ownership stays a
/// handler-level check.
/// </summary>
public sealed class CreateCombinedGoalsValidator : Validator<CreateCombinedGoalsRequest>
{
    private const int MaxGoals = 4;

    public CreateCombinedGoalsValidator()
    {
        RuleFor(request => request.EntityType)
            .Must(value => Enum.TryParse<GoalEntityType>(value, ignoreCase: true, out _))
            .WithMessage("Unknown entity type.");

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
                .Must(value => Enum.TryParse<GoalType>(value, ignoreCase: true, out _))
                .WithMessage("Unknown or not-yet-supported goal type."));

        RuleForEach(request => request.Goals)
            .ChildRules(goal => goal.RuleFor(spec => spec.Snapshot)
                .Must(CreateGoalValidator.IsValidSnapshot)
                .WithMessage("Snapshot resource ids are required and counts cannot be negative."));

        RuleForEach(request => request.Goals)
            .ChildRules(goal => goal.RuleFor(spec => spec.Config)
                .Must(CreateGoalValidator.IsValidConfig)
                .WithMessage("The farming strategy or ascension farming configuration is invalid."));

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

        RuleFor(request => request)
            .Must(request => !IsMow(request.EntityType) || request.Goals.TrueForAll(spec => !IsRank(spec.GoalType)))
            .WithMessage("Machines of War have no rank — use an Ability goal instead.");
    }

    private static bool IsMow(string entityType) =>
        Enum.TryParse<GoalEntityType>(entityType, ignoreCase: true, out var value)
            && value == GoalEntityType.Mow;

    private static bool IsRank(string goalType) =>
        Enum.TryParse<GoalType>(goalType, ignoreCase: true, out var value) && value == GoalType.Rank;
}
