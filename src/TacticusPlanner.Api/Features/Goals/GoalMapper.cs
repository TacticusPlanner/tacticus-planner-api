using System.Text.Json.Serialization;
using FastEndpoints;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.GameDomain;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Maps <see cref="Goal"/> to its single served shape (<see cref="GoalDetailResponse"/>: the goals list, the goal
/// read and every mutation answer with it) and the creation request to the domain config. Used through the shared
/// <c>Map</c> property (FastEndpoints only requires <c>TMapper : IResponseMapper</c>, so one instance serves every
/// goal endpoint).
/// </summary>
public sealed class GoalMapper : ResponseMapper<GoalDetailResponse, Goal>
{
    /// <summary>Satisfies the base <c>ResponseMapper</c> contract only — every real call site knows the goal's
    /// current project membership and calls <see cref="ToDetail"/> instead.</summary>
    public override GoalDetailResponse FromEntity(Goal goal) => ToDetail(goal, []);

    /// <summary>The goal->response mapping. Takes <paramref name="projectIds"/> because <see cref="Goal"/> has no
    /// navigation to its <c>ProjectGoal</c> memberships (that join lives on the project side); they are served
    /// sorted, so every response carrying a goal agrees on them.</summary>
    public GoalDetailResponse ToDetail(Goal goal, List<Guid> projectIds) => new(
        goal.Id.Value,
        goal.EntityType,
        goal.EntityId,
        goal.GoalType,
        goal.Status,
        goal.Notes,
        BuildConfig(goal.Config),
        goal.Snapshot is null ? null : BuildSnapshot(goal.Snapshot),
        goal.DependsOn.ToList(),
        projectIds.Order().ToList(),
        goal.CreatedAt,
        goal.UpdatedAt,
        goal.Revision,
        goal.GlobalPriority
    );

    /// <summary>The request config of a goal being created, in its domain form.</summary>
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
            Start = ProgressionRules.WireName(config.Progression.Start),
            End = ProgressionRules.WireName(config.Progression.End),
        },
        Ability = config.Ability is null ? null : new AbilityTarget
        {
            ActiveStart = config.Ability.ActiveStart,
            ActiveEnd = config.Ability.ActiveEnd,
            PassiveStart = config.Ability.PassiveStart,
            PassiveEnd = config.Ability.PassiveEnd,
        },
        FarmingLocationIds = config.FarmingLocationIds?.Select(id => id.Value).ToList(),
        FarmingStrategy = config.FarmingStrategy ?? FarmingStrategy.TotalUpgrades,
        AcquisitionSources = MapAcquisitionSources(config.AcquisitionSources),
        Upgrade = config.Upgrade is null ? null : new UpgradeTarget
        {
            Targets = config.Upgrade.Targets.Select(target => new UpgradeMaterialTarget
            {
                UpgradeId = target.UpgradeId.Trim(),
                Quantity = target.Quantity,
            }).ToList(),
            RankRange = ToRange(config.Upgrade.RankRange),
            ActiveRange = ToRange(config.Upgrade.ActiveRange),
            PassiveRange = ToRange(config.Upgrade.PassiveRange),
        },
    };

    internal static UpgradeRange? ToRange(UpgradeRangeRequest? range) =>
        range is { Start: { } start, End: { } end } ? new UpgradeRange { Start = start, End = end } : null;

    private static UpgradeRangeResponse? ToResponse(UpgradeRange? range) =>
        range is null ? null : new UpgradeRangeResponse(range.Start, range.End);

    /// <summary>Maps the wire acquisition-source list to its domain form. Null / empty stays null —
    /// "unrestricted campaign farming", the pre-picker default. Shared by <see cref="MapConfig"/> and
    /// <see cref="GoalDetailsEditor"/>.</summary>
    internal static List<AcquisitionSource>? MapAcquisitionSources(List<AcquisitionSourceRequest>? sources) =>
        sources is not { Count: > 0 }
            ? null
            : sources.Select(source => new AcquisitionSource
            {
                Kind = source.Kind.ToString(),
                Ids = (source.Ids ?? []).Select(id => id.Trim()).ToList(),
            }).ToList();

    internal static GoalSnapshot MapSnapshot(CreateGoalSnapshotRequest? snapshot) => new()
    {
        InitialRank = snapshot?.InitialRank,
        InitialProgression = snapshot?.InitialProgression,
        InitialActiveAbilityLevel = snapshot?.InitialActiveAbilityLevel,
        InitialPassiveAbilityLevel = snapshot?.InitialPassiveAbilityLevel,
        InitialRequirement = MapResources(snapshot?.InitialRequirement),
        InitialInventoryContribution = MapResources(snapshot?.InitialInventoryContribution),
    };

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
        config.Progression is null ? null : new ProgressionTargetResponse(
            ProgressionRules.FromWireName(config.Progression.Start), ProgressionRules.FromWireName(config.Progression.End)),
        config.Ability is null ? null : new AbilityTargetResponse(
            config.Ability.ActiveStart,
            config.Ability.ActiveEnd,
            config.Ability.PassiveStart,
            config.Ability.PassiveEnd
        ),
        config.FarmingLocationIds,
        config.FarmingStrategy,
        // Normalize empty-or-null to null (like MapAcquisitionSources does on the way in) — EF's
        // change-tracking snapshot can coerce a never-set owned collection navigation from null to an
        // empty list once the entity is tracked, and both mean the same "unrestricted campaign" here.
        config.AcquisitionSources is not { Count: > 0 }
            ? null
            : config.AcquisitionSources.Select(source => new AcquisitionSourceResponse(Enum.Parse<AcquisitionSourceKind>(source.Kind), source.Ids)).ToList(),
        config.Upgrade is null ? null : new UpgradeTargetResponse(
            config.Upgrade.Targets.Select(target => new UpgradeMaterialTargetResponse(target.UpgradeId, target.Quantity)).ToList(),
            ToResponse(config.Upgrade.RankRange),
            ToResponse(config.Upgrade.ActiveRange),
            ToResponse(config.Upgrade.PassiveRange))
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

}

public sealed record GoalDetailResponse(
    Guid GoalId,
    GoalEntityType EntityType,
    string EntityId,
    GoalType GoalType,
    GoalStatus Status,
    string? Notes,
    GoalConfigResponse Config,
    GoalSnapshotResponse? Snapshot,
    List<Guid> DependsOn,
    // The ids of every project this goal currently belongs to (a goal may belong to several projects).
    List<Guid> ProjectIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Monotonically increasing; a target edit must echo it as expectedRevision (optimistic concurrency).
    [property: JsonRequired] long Revision = 0,
    [property: JsonRequired] int? GlobalPriority = null
);

public sealed record GoalConfigResponse(
    RankTargetResponse? Rank,
    ProgressionTargetResponse? Progression,
    AbilityTargetResponse? Ability,
    List<string>? FarmingLocationIds,
    FarmingStrategy FarmingStrategy,
    List<AcquisitionSourceResponse>? AcquisitionSources,
    UpgradeTargetResponse? Upgrade
);

public sealed record AcquisitionSourceResponse(AcquisitionSourceKind Kind, List<string> Ids);

public sealed record UpgradeTargetResponse(
    List<UpgradeMaterialTargetResponse> Targets,
    [property: JsonRequired] UpgradeRangeResponse? RankRange = null,
    [property: JsonRequired] UpgradeRangeResponse? ActiveRange = null,
    [property: JsonRequired] UpgradeRangeResponse? PassiveRange = null);

public sealed record UpgradeRangeResponse(int Start, int End);

public sealed record UpgradeMaterialTargetResponse(string UpgradeId, int Quantity);

public sealed record RankTargetResponse(
    int Start,
    bool StartPointFive,
    int StartAppliedUpgrades,
    int End,
    bool EndPointFive,
    int EndAppliedUpgrades
);

public sealed record ProgressionTargetResponse(UnitProgression Start, UnitProgression End);

public sealed record AbilityTargetResponse(int ActiveStart, int ActiveEnd, int PassiveStart, int PassiveEnd);

public sealed record GoalSnapshotResponse(
    UnitRank? InitialRank,
    UnitProgression? InitialProgression,
    int? InitialActiveAbilityLevel,
    int? InitialPassiveAbilityLevel,
    List<GoalSnapshotResourceResponse> InitialRequirement,
    List<GoalSnapshotResourceResponse> InitialInventoryContribution
);

public sealed record GoalSnapshotResourceResponse(string ResourceId, int Count);

