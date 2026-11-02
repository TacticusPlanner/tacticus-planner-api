using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Domain.RosterSnapshots;
using TacticusPlanner.Domain.UserSettings;

namespace TacticusPlanner.Api.Features.StagingTransfer;

/// <summary>
/// One production profile's planner data, as production exports it and staging imports it. Staging runs newer
/// code than production, so the bundle is read leniently (unknown members ignored, missing ones defaulted) and
/// <see cref="FormatVersion"/> is bumped only for a change an older reader would misinterpret.
/// </summary>
/// <remarks>
/// Deliberately absent: the Tacticus API key and user id (secrets the tester re-enters on staging), the
/// player-data snapshot (re-synced from Tacticus), and guild data (guild-scoped, registered with a guild key).
/// Ids are production's; staging assigns its own on import and rewrites every reference.
/// </remarks>
public sealed record StagingTransferBundle
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public DateTimeOffset ExportedAt { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public UserSettingsData? Settings { get; init; }

    public StagingTransferOverrides? Overrides { get; init; }

    public List<StagingTransferGoal> Goals { get; init; } = [];

    public List<StagingTransferProject> Projects { get; init; } = [];

    public List<StagingTransferLegendaryEventPlan> LegendaryEventPlans { get; init; } = [];

    public List<StagingTransferLegendaryEventRunRecord> LegendaryEventRunRecords { get; init; } = [];
}

public sealed record StagingTransferOverrides
{
    public List<CampaignEventProgressOverrideRecord> CampaignEventProgress { get; init; } = [];

    public List<OnslaughtProgressOverrideRecord> OnslaughtProgress { get; init; } = [];

    public List<BattleProgressOverrideRecord> BattleProgress { get; init; } = [];

    public List<IncursionBattleProgressOverrideRecord> IncursionBattleProgress { get; init; } = [];
}

public sealed record StagingTransferGoal
{
    public Guid Id { get; init; }

    public GoalEntityType EntityType { get; init; }

    public string EntityId { get; init; } = string.Empty;

    public GoalType GoalType { get; init; }

    public GoalStatus Status { get; init; }

    public int? GlobalPriority { get; init; }

    public string? Notes { get; init; }

    public GoalConfig Config { get; init; } = new();

    public GoalSnapshot? Snapshot { get; init; }

    public List<Guid> DependsOn { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record StagingTransferProject
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? Color { get; init; }

    public ProjectStatus Status { get; init; }

    public ProjectType Type { get; init; }

    public ProjectTargetKind? TargetKind { get; init; }

    public string? TargetEventId { get; init; }

    public List<Guid> GoalIds { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record StagingTransferLegendaryEventPlan
{
    public string EventId { get; init; } = string.Empty;

    public string CatalogVersion { get; init; } = string.Empty;

    public string? Notes { get; init; }

    public bool ShowPaidOptions { get; init; }

    public List<LegendaryEventTeamRecord> Teams { get; init; } = [];

    public List<LegendaryEventRunProgress> Runs { get; init; } = [];

    public List<LegendaryEventObjectiveAnnotation> Annotations { get; init; } = [];
}

public sealed record StagingTransferLegendaryEventRunRecord
{
    public string EventId { get; init; } = string.Empty;

    public int Run { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset ProgressAt { get; init; }

    public string CatalogVersion { get; init; } = string.Empty;

    public int Points { get; init; }

    public LegendaryEventRunRecordLanes Lanes { get; init; } = new();

    public List<LegendaryEventTeamRecord> Teams { get; init; } = [];

    public RosterSnapshot Roster { get; init; } = new();
}
