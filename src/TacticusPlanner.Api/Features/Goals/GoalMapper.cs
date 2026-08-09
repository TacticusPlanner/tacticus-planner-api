using System.Reflection;
using System.Text.Json.Serialization;
using FastEndpoints;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.GameDomain;

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
    /// <summary>Wire-format lookup for <see cref="UnitProgression"/>, built from each member's
    /// <see cref="JsonStringEnumMemberNameAttribute"/> — the client sends the same "{Rarity}:{Stars}"
    /// strings the frontend's <c>Progression</c> union uses (e.g. "Common:TwoStars"), which
    /// <c>Enum.TryParse</c> can't match against the C# member names directly.</summary>
    private static readonly Dictionary<string, UnitProgression> ProgressionByWireValue =
        typeof(UnitProgression).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (
                Name: field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name,
                Value: (UnitProgression)field.GetValue(null)!))
            .Where(entry => entry.Name is not null)
            .GroupBy(entry => entry.Name!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);

    public override Goal ToEntity(CreateGoalRequest r) => new()
    {
        EntityId = r.EntityId.Trim(),
        Config = MapConfig(r.Config),
    };

    /// <summary>Satisfies the base <c>Mapper</c> contract only — every real call site knows the goal's
    /// current project membership and should call <see cref="ToDetail"/> instead, which is why this
    /// override delegates with an empty <c>ProjectIds</c> rather than being used directly.</summary>
    public override GoalDetailResponse FromEntity(Goal goal) => ToDetail(goal, []);

    /// <summary>The actual goal->response mapping. Takes <paramref name="projectIds"/> as a parameter —
    /// like <see cref="Projects.ProjectMapper.ToSummary"/> takes the active-project id — because
    /// <see cref="Goal"/> itself has no navigation to its <c>ProjectGoal</c> memberships (plan §5: that
    /// join lives entirely on the project side).</summary>
    public GoalDetailResponse ToDetail(Goal goal, List<Guid> projectIds) => new(
        goal.Id.Value,
        goal.EntityType.ToString(),
        goal.EntityId,
        goal.GoalType.ToString(),
        goal.Status.ToString(),
        goal.Notes,
        BuildConfig(goal.Config),
        goal.Snapshot is null ? null : BuildSnapshot(goal.Snapshot),
        goal.Events.Select(BuildEvent).ToList(),
        goal.DependsOn.ToList(),
        projectIds,
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
        FarmingLocationIds = config.FarmingLocationIds?.Select(id => id.Value).ToList(),
        FarmingStrategy = Enum.TryParse<FarmingStrategy>(config.FarmingStrategy, ignoreCase: true, out var strategy)
            ? strategy
            : FarmingStrategy.TotalUpgrades,
        AscensionFarming = config.AscensionFarming is null ? null : new AscensionFarmingConfig
        {
            Source = Enum.Parse<AscensionFarmingSource>(config.AscensionFarming.Source, ignoreCase: true),
            ShardBattleIds = config.AscensionFarming.ShardBattleIds.Select(id => id.Value).ToList(),
            MythicShardBattleIds = config.AscensionFarming.MythicShardBattleIds.Select(id => id.Value).ToList(),
        },
        Upgrade = config.Upgrade is null ? null : new UpgradeTarget
        {
            Targets = config.Upgrade.Targets.Select(target => new UpgradeMaterialTarget
            {
                UpgradeId = target.UpgradeId.Trim(),
                Quantity = target.Quantity,
            }).ToList(),
        },
        Level = config.Level is null ? null : new LevelTarget
        {
            Start = config.Level.Start,
            End = config.Level.End,
        },
    };

    internal static GoalSnapshot MapSnapshot(CreateGoalSnapshotRequest? snapshot) => new()
    {
        InitialRank = ParseRank(snapshot?.InitialRank),
        InitialProgression = ParseProgression(snapshot?.InitialProgression),
        InitialActiveAbilityLevel = snapshot?.InitialActiveAbilityLevel,
        InitialPassiveAbilityLevel = snapshot?.InitialPassiveAbilityLevel,
        InitialRequirement = MapResources(snapshot?.InitialRequirement),
        InitialInventoryContribution = MapResources(snapshot?.InitialInventoryContribution),
    };

    /// <summary>Best-effort — an unparseable client-supplied value becomes null rather than a 400; the
    /// snapshot is a display baseline, not authoritative game state.</summary>
    private static UnitRank? ParseRank(string? value) =>
        value is not null && Enum.TryParse<UnitRank>(value, ignoreCase: true, out var rank) ? rank : null;

    private static UnitProgression? ParseProgression(string? value) =>
        value is not null && ProgressionByWireValue.TryGetValue(value, out var progression) ? progression : null;

    private static List<GoalSnapshotResource> MapResources(List<GoalSnapshotResourceRequest>? resources) =>
        resources?.Select(resource => new GoalSnapshotResource
        {
            ResourceId = resource.ResourceId.Trim(),
            Count = resource.Count,
        }).ToList() ?? [];

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
        config.FarmingLocationIds,
        config.FarmingStrategy.ToString(),
        config.AscensionFarming is null ? null : new AscensionFarmingResponse(
            config.AscensionFarming.Source.ToString(),
            config.AscensionFarming.ShardBattleIds,
            config.AscensionFarming.MythicShardBattleIds),
        config.Upgrade is null ? null : new UpgradeTargetResponse(
            config.Upgrade.Targets.Select(target => new UpgradeMaterialTargetResponse(target.UpgradeId, target.Quantity)).ToList()),
        config.Level is null ? null : new LevelTargetResponse(config.Level.Start, config.Level.End)
    );

    private static GoalSnapshotResponse BuildSnapshot(GoalSnapshot snapshot) => new(
        snapshot.InitialRank,
        snapshot.InitialProgression,
        snapshot.InitialActiveAbilityLevel,
        snapshot.InitialPassiveAbilityLevel,
        snapshot.InitialRequirement.Select(BuildSnapshotResource).ToList(),
        snapshot.InitialInventoryContribution.Select(BuildSnapshotResource).ToList()
    );

    private static GoalSnapshotResourceResponse BuildSnapshotResource(GoalSnapshotResource resource) =>
        new(resource.ResourceId, resource.Count);

    private static GoalEventResponse BuildEvent(GoalEvent goalEvent) => new(goalEvent.At, goalEvent.Type);
}

public sealed record GoalSummaryResponse(
    Guid GoalId,
    string EntityType,
    string EntityId,
    string GoalType,
    string Status,
    string? Notes,
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
    GoalSnapshotResponse? Snapshot,
    List<GoalEventResponse> Events,
    List<Guid> DependsOn,
    // The ids of every project this goal currently belongs to (plan: a goal may belong to several
    // projects at once). Populated by the caller via GoalMapper.ToDetail — Goal itself has no navigation
    // to ProjectGoal, so this can't be filled in by FromEntity(Goal) alone.
    List<Guid> ProjectIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record GoalConfigResponse(
    RankTargetResponse? Rank,
    ProgressionTargetResponse? Progression,
    AbilityTargetResponse? Ability,
    List<string>? FarmingLocationIds,
    string FarmingStrategy,
    AscensionFarmingResponse? AscensionFarming,
    UpgradeTargetResponse? Upgrade,
    LevelTargetResponse? Level
);

public sealed record UpgradeTargetResponse(List<UpgradeMaterialTargetResponse> Targets);

public sealed record UpgradeMaterialTargetResponse(string UpgradeId, int Quantity);

public sealed record LevelTargetResponse(int Start, int End);

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

public sealed record AscensionFarmingResponse(
    string Source,
    List<string> ShardBattleIds,
    List<string> MythicShardBattleIds
);

public sealed record GoalSnapshotResponse(
    UnitRank? InitialRank,
    UnitProgression? InitialProgression,
    int? InitialActiveAbilityLevel,
    int? InitialPassiveAbilityLevel,
    List<GoalSnapshotResourceResponse> InitialRequirement,
    List<GoalSnapshotResourceResponse> InitialInventoryContribution
);

public sealed record GoalSnapshotResourceResponse(string ResourceId, int Count);

public sealed record GoalEventResponse(DateTimeOffset At, GoalEventType Type);
