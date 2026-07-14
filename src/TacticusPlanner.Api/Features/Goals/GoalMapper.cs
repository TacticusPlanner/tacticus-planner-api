using FastEndpoints;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Maps between <see cref="Goal"/> and its API request/response shapes. <see cref="ToEntity"/> is used
/// only by <see cref="CreateGoalEndpoint"/> (the sole place a goal is constructed from a request);
/// <see cref="FromEntity"/> and <see cref="ToSummary"/> are used by every Goals endpoint that returns a
/// goal, via the shared <c>Map</c> property (FastEndpoints only requires <c>TMapper : IMapper</c> /
/// <c>IResponseMapper</c> — it does not require the endpoint's own request/response types to match this
/// mapper's, so one instance safely serves Create/Get/List/Update/UpdateStatus).
/// </summary>
public sealed class GoalMapper : Mapper<CreateGoalRequest, GoalDetailResponse, Goal>
{
    public override Goal ToEntity(CreateGoalRequest r) => new()
    {
        EntityId = r.EntityId.Trim(),
        Config = MapConfig(r.Config),
    };

    public override GoalDetailResponse FromEntity(Goal goal) => new(
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

    public GoalSummaryResponse ToSummary(Goal goal) => new(
        goal.Id.Value,
        goal.EntityType.ToString(),
        goal.EntityId,
        goal.GoalType.ToString(),
        goal.Status.ToString(),
        goal.Notes,
        goal.AggregateId,
        goal.Milestones.Count,
        goal.Milestones.Count(milestone => milestone.Status == "completed"),
        goal.CreatedAt,
        goal.UpdatedAt
    );

    /// <summary>Also used directly by <see cref="CreateCombinedGoalsEndpoint"/>, which builds its own
    /// <see cref="Goal"/> instances (a combined request creates several goals per call, not one, so it
    /// doesn't go through <see cref="ToEntity"/>).</summary>
    internal static GoalConfig MapConfig(CreateGoalConfigRequest config) => new()
    {
        Rank = config.Rank is null ? null : new RankTarget
        {
            Start = config.Rank.Start,
            StartPointFive = config.Rank.StartPointFive,
            StartAppliedUpgrades = config.Rank.StartAppliedUpgrades,
            End = config.Rank.End,
            EndPointFive = config.Rank.EndPointFive,
            EndAppliedUpgrades = config.Rank.EndAppliedUpgrades,
        },
        Progression = config.Progression is null ? null : new ProgressionTarget
        {
            Start = config.Progression.Start,
            End = config.Progression.End,
        },
        Ability = config.Ability is null ? null : new AbilityTarget
        {
            ActiveStart = config.Ability.ActiveStart,
            ActiveEnd = config.Ability.ActiveEnd,
            PassiveStart = config.Ability.PassiveStart,
            PassiveEnd = config.Ability.PassiveEnd,
        },
        Shards = config.Shards is null ? null : new ShardTarget { Count = config.Shards.Count },
        FarmingLocationIds = config.FarmingLocationIds?.Select(id => id.Value).ToList(),
    };

    private static GoalConfigResponse BuildConfig(GoalConfig config) => new(
        config.Rank is null ? null : new RankTargetResponse(
            config.Rank.Start,
            config.Rank.StartPointFive,
            config.Rank.StartAppliedUpgrades,
            config.Rank.End,
            config.Rank.EndPointFive,
            config.Rank.EndAppliedUpgrades
        ),
        config.Progression is null ? null : new ProgressionTargetResponse(config.Progression.Start, config.Progression.End),
        config.Ability is null ? null : new AbilityTargetResponse(
            config.Ability.ActiveStart,
            config.Ability.ActiveEnd,
            config.Ability.PassiveStart,
            config.Ability.PassiveEnd
        ),
        config.Shards is null ? null : new ShardTargetResponse(config.Shards.Count),
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
    // Lean milestone counts (not the full list — see GoalDetailResponse.Milestones for that) so a
    // goals list can show "M/N" without an N+1 detail fetch per row.
    int MilestonesTotal,
    int MilestonesCompleted,
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
    RankTargetResponse? Rank,
    ProgressionTargetResponse? Progression,
    AbilityTargetResponse? Ability,
    ShardTargetResponse? Shards,
    List<string>? FarmingLocationIds
);

public sealed record RankTargetResponse(
    int Start,
    bool StartPointFive,
    int StartAppliedUpgrades,
    int End,
    bool EndPointFive,
    int EndAppliedUpgrades
);

public sealed record ProgressionTargetResponse(string Start, string End);

public sealed record AbilityTargetResponse(int ActiveStart, int ActiveEnd, int PassiveStart, int PassiveEnd);

public sealed record ShardTargetResponse(int Count);

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

public sealed record GoalEventResponse(DateTimeOffset At, GoalEventType Type);
