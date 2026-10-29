using FastEndpoints;
using FluentValidation;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Request-shape rules only (entity and goal types are enums, rejected at binding when unknown) — the list
/// must be non-empty and reasonably small (Unlock + Ascension + a milestone ladder of a few Rank or Ability
/// steps is the largest realistic combined request), every <see cref="CombinedGoalSpec.DependsOnIndex"/> must reference a strictly
/// earlier spec in the same request (no forward or self references), and a Machine of War request may
/// not include a Rank goal (MoWs have no rank ladder — plan §16 phase 6). Project ownership stays a
/// handler-level check.
/// </summary>
public sealed class CreateCombinedGoalsValidator : Validator<CreateCombinedGoalsRequest>
{
    private const int MaxGoals = 10;

    public CreateCombinedGoalsValidator()
    {
        RuleFor(request => request.EntityId)
            .NotEmpty()
            .WithMessage("An entity id is required.")
            .MaximumLength(GoalValidation.MaxEntityIdLength);

        RuleFor(request => request.Goals)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("At least one goal is required.")
            .Must(goals => goals.Count <= MaxGoals)
            .WithMessage($"A combined request can create at most {MaxGoals} goals.");

        RuleForEach(request => request.Goals)
            .ChildRules(goal => goal.RuleFor(spec => spec.Snapshot)
                .Must(IsValidSnapshot)
                .WithMessage("Snapshot resource ids are required and counts cannot be negative."));

        RuleForEach(request => request.Goals)
            .ChildRules(goal => goal.RuleFor(spec => spec.Snapshot)
                .Must(snapshot => IsBoundedResourceList(snapshot?.InitialRequirement)
                    && IsBoundedResourceList(snapshot?.InitialInventoryContribution))
                .WithMessage($"Each snapshot resource list holds at most {GoalValidation.MaxResourcesPerGoal} distinct "
                    + $"resource ids of at most {GoalValidation.MaxEntityIdLength} characters."));

        RuleForEach(request => request.Goals)
            .ChildRules(goal => goal.RuleFor(spec => spec.Config)
                .Must(config => config?.FarmingLocationIds is not { Count: > GoalValidation.MaxFarmingLocations })
                .WithMessage($"A goal can have at most {GoalValidation.MaxFarmingLocations} farming locations."));

        RuleForEach(request => request.Goals)
            .ChildRules(goal => goal.RuleFor(spec => spec.Config)
                .Must(config => config?.Upgrade?.Targets is not { Count: > GoalValidation.MaxResourcesPerGoal })
                .WithMessage($"An upgrade goal can have at most {GoalValidation.MaxResourcesPerGoal} targets."));

        RuleForEach(request => request.Goals)
            .ChildRules(goal => goal.RuleFor(spec => spec.Config)
                .Must(config => UpgradeRangeRules.HasWholeRanges(config?.Upgrade))
                .WithMessage(UpgradeRangeRules.ShapeMessage));

        RuleForEach(request => request.Goals)
            .ChildRules(goal => goal.RuleFor(spec => spec.Config)
                .Must(config => AcquisitionSourceRules.ShapeError(config?.AcquisitionSources) is null)
                .WithMessage(spec => AcquisitionSourceRules.ShapeError(spec.Config?.AcquisitionSources)
                    ?? "The acquisition sources configuration is invalid."));

        RuleFor(request => request.Goals)
            .Custom((goals, context) =>
            {
                if (goals is null) return;
                for (var i = 0; i < goals.Count; i++)
                {
                    var dependsOn = goals[i].DependsOnIndex ?? [];
                    if (dependsOn.Any(dependsOnIndex => dependsOnIndex < 0 || dependsOnIndex >= i))
                    {
                        context.AddFailure(
                            $"Goals[{i}].DependsOnIndex",
                            "Each DependsOnIndex must reference an earlier goal in the same request."
                        );
                    }
                    else if (dependsOn.Count > MaxGoals || dependsOn.Distinct().Count() != dependsOn.Count)
                    {
                        context.AddFailure($"Goals[{i}].DependsOnIndex", "DependsOnIndex entries must be distinct.");
                    }
                }
            });

        RuleForEach(request => request.Goals)
            .ChildRules(goal => goal.RuleFor(spec => spec.Notes).MaximumLength(GoalValidation.MaxNotesLength));

        RuleForEach(request => request.Goals)
            .ChildRules(goal => goal.RuleFor(spec => spec.DependsOnGoalIds)
                .Must(ids => ids is null || (ids.TrueForAll(id => id != Guid.Empty) && ids.Distinct().Count() == ids.Count))
                .WithMessage("DependsOnGoalIds must be distinct, non-empty goal ids."));

        RuleFor(request => request)
            .Must(request => request.EntityType != GoalEntityType.Mow
                || request.Goals is null
                || request.Goals.TrueForAll(spec => spec.GoalType != GoalType.Rank))
            .WithMessage("Machines of War have no rank — use an Ability goal instead.");

    }

    internal static bool IsValidSnapshot(CreateGoalSnapshotRequest? snapshot)
    {
        if (snapshot is null) return true;
        return snapshot.InitialRequirement?.All(IsValidResource) != false
            && snapshot.InitialInventoryContribution?.All(IsValidResource) != false;
    }

    /// <summary>Bounded in count and id length, each id once (compared trimmed, as stored).</summary>
    internal static bool IsBoundedResourceList(List<GoalSnapshotResourceRequest>? resources) =>
        resources is null
        || (resources.Count <= GoalValidation.MaxResourcesPerGoal
            && resources.TrueForAll(resource => resource?.ResourceId is not { Length: > GoalValidation.MaxEntityIdLength })
            && resources.Select(resource => resource?.ResourceId?.Trim()).Distinct(StringComparer.Ordinal).Count() == resources.Count);

    private static bool IsValidResource(GoalSnapshotResourceRequest resource) =>
        !string.IsNullOrWhiteSpace(resource.ResourceId) && resource.Count >= 0;
}
