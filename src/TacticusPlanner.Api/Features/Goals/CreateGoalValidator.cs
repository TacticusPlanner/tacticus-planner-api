using FastEndpoints;
using FluentValidation;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Request-shape rules only — entity/goal type must parse to a supported value, an
/// entity id is required, and a Machine of War may not be given a Rank goal (MoWs have no rank
/// ladder — plan §16 phase 6). Project ownership (a DB lookup) stays a handler-level check.
/// </summary>
public sealed class CreateGoalValidator : Validator<CreateGoalRequest>
{
    public CreateGoalValidator()
    {
        RuleFor(request => request.EntityType)
            .Must(value => Enum.TryParse<GoalEntityType>(value, ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed)
                && !int.TryParse(value, out _))
            .WithMessage("Unknown entity type.");

        RuleFor(request => request.GoalType)
            .Must(value => Enum.TryParse<GoalType>(value, ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed)
                && !int.TryParse(value, out _))
            .WithMessage("Unknown or not-yet-supported goal type.");

        RuleFor(request => request.EntityId)
            .NotEmpty()
            .WithMessage("An entity id is required.")
            .MaximumLength(GoalValidation.MaxEntityIdLength);

        // Machines of War have no rank ladder (plan §16 phase 6) — reject the combination before it
        // reaches MilestoneGenerator, which would otherwise stamp character-rank milestones onto it.
        RuleFor(request => request)
            .Must(request => !(IsMow(request.EntityType) && IsRank(request.GoalType)))
            .WithMessage("Machines of War have no rank — use an Ability goal instead.");

        RuleFor(request => request.Snapshot)
            .Must(IsValidSnapshot)
            .WithMessage("Snapshot resource ids are required and counts cannot be negative.");

        RuleFor(request => request.Config)
            .NotNull()
            .WithMessage("A goal configuration is required.")
            .Must(IsValidConfig)
            .WithMessage("The farming strategy or ascension farming configuration is invalid.");

    }

    private static bool IsMow(string entityType) =>
        Enum.TryParse<GoalEntityType>(entityType, ignoreCase: true, out var value)
            && value == GoalEntityType.Mow;

    private static bool IsRank(string goalType) =>
        Enum.TryParse<GoalType>(goalType, ignoreCase: true, out var value) && value == GoalType.Rank;

    internal static bool IsValidSnapshot(CreateGoalSnapshotRequest? snapshot)
    {
        if (snapshot is null) return true;
        return snapshot.InitialRequirement?.All(IsValidResource) != false
            && snapshot.InitialInventoryContribution?.All(IsValidResource) != false;
    }

    private static bool IsValidResource(GoalSnapshotResourceRequest resource) =>
        !string.IsNullOrWhiteSpace(resource.ResourceId) && resource.Count >= 0;

    internal static bool IsValidConfig(CreateGoalConfigRequest? config)
    {
        if (config is null) return true; // NotNull() reports the missing config
        if (config.FarmingStrategy is not null
            && (!Enum.TryParse<FarmingStrategy>(config.FarmingStrategy, ignoreCase: true, out var strategy)
                || !Enum.IsDefined(strategy)
                || int.TryParse(config.FarmingStrategy, out _)))
        {
            return false;
        }

        var farming = config.AscensionFarming;
        return farming is null
            || (farming.ShardBattleIds is not null
                && farming.MythicShardBattleIds is not null
                && Enum.TryParse<AscensionFarmingSource>(farming.Source, ignoreCase: true, out var source)
                && Enum.IsDefined(source)
                && !int.TryParse(farming.Source, out _));
    }
}
