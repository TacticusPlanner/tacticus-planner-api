using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>Maps <see cref="Goal"/> domain entities to API response shapes. Applied in-memory after
/// materialization (never inside an EF <c>.Select()</c> that would need SQL translation).</summary>
public static class GoalProjection
{
    public static GoalSummaryResponse BuildSummary(Goal goal) => new(
        goal.Id.Value,
        goal.EntityType.ToString(),
        goal.EntityId,
        goal.GoalType.ToString(),
        goal.Status.ToString(),
        goal.Notes,
        goal.AggregateId,
        goal.CreatedAt,
        goal.UpdatedAt
    );

    public static GoalDetailResponse BuildDetail(Goal goal) => new(
        goal.Id.Value,
        goal.EntityType.ToString(),
        goal.EntityId,
        goal.GoalType.ToString(),
        goal.Status.ToString(),
        goal.Notes,
        BuildConfig(goal.Config),
        goal.Milestones.Select(BuildMilestone).ToList(),
        goal.Snapshot is null ? null : BuildSnapshot(goal.Snapshot),
        goal.Events.Select(BuildEvent).ToList(),
        goal.DependsOn.ToList(),
        goal.AggregateId,
        goal.CreatedAt,
        goal.UpdatedAt
    );

    private static GoalConfigResponse BuildConfig(GoalConfig config) => new(
        config.RankStart,
        config.RankStartPointFive,
        config.RankStartAppliedUpgrades,
        config.RankEnd,
        config.RankEndPointFive,
        config.RankEndAppliedUpgrades,
        config.ProgressionStart,
        config.ProgressionEnd,
        config.AbilityActiveStart,
        config.AbilityActiveEnd,
        config.AbilityPassiveStart,
        config.AbilityPassiveEnd,
        config.ShardsTarget,
        config.FarmingMode,
        config.FarmingLocationIds
    );

    private static GoalMilestoneResponse BuildMilestone(GoalMilestone milestone) => new(
        milestone.Index,
        milestone.Kind,
        milestone.TargetState,
        milestone.Source,
        milestone.Status,
        milestone.CompletedAt
    );

    private static GoalSnapshotResponse BuildSnapshot(GoalSnapshot snapshot) => new(
        snapshot.CreatedAt,
        snapshot.OriginalEstimateDays,
        snapshot.OriginalEstimateDate
    );

    private static GoalEventResponse BuildEvent(GoalEvent goalEvent) => new(goalEvent.At, goalEvent.Type);
}

public sealed record GoalSummaryResponse(
    Guid GoalId,
    string EntityType,
    string EntityId,
    string GoalType,
    string Status,
    string? Notes,
    Guid? AggregateId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record GoalDetailResponse(
    Guid GoalId,
    string EntityType,
    string EntityId,
    string GoalType,
    string Status,
    string? Notes,
    GoalConfigResponse Config,
    List<GoalMilestoneResponse> Milestones,
    GoalSnapshotResponse? Snapshot,
    List<GoalEventResponse> Events,
    List<Guid> DependsOn,
    Guid? AggregateId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record GoalConfigResponse(
    int? RankStart,
    bool? RankStartPointFive,
    int? RankStartAppliedUpgrades,
    int? RankEnd,
    bool? RankEndPointFive,
    int? RankEndAppliedUpgrades,
    string? ProgressionStart,
    string? ProgressionEnd,
    int? AbilityActiveStart,
    int? AbilityActiveEnd,
    int? AbilityPassiveStart,
    int? AbilityPassiveEnd,
    int? ShardsTarget,
    string? FarmingMode,
    List<string>? FarmingLocationIds
);

public sealed record GoalMilestoneResponse(
    int Index,
    string Kind,
    string TargetState,
    string Source,
    string Status,
    DateTimeOffset? CompletedAt
);

public sealed record GoalSnapshotResponse(
    DateTimeOffset CreatedAt,
    int? OriginalEstimateDays,
    DateTimeOffset? OriginalEstimateDate
);

public sealed record GoalEventResponse(DateTimeOffset At, string Type);
