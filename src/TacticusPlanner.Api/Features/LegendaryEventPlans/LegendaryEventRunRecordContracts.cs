using TacticusPlanner.Domain.PlayerData.Chunks;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

/// <summary>Every stored plan of the caller's profile (LRE Stage 7, design D5).</summary>
/// <param name="Plans">Ordered by <c>eventId</c> (ordinal), each exactly as its single <c>GET</c> returns it.</param>
public sealed record LegendaryEventPlanListResponse(IReadOnlyList<LegendaryEventPlanResponse> Plans);

/// <summary>The caller's run records for one event (LRE Stage 7, design D6).</summary>
/// <param name="Records">Newest <c>startedAt</c> first.</param>
public sealed record LegendaryEventRunRecordsResponse(string EventId, IReadOnlyList<LegendaryEventRunRecordResponse> Records);

/// <summary>One run occurrence as of the last sync that changed its progress.</summary>
/// <param name="Run">The event run, 1–3.</param>
/// <param name="StartedAt">The first sync that saw this occurrence (UTC).</param>
/// <param name="ProgressAt">The last sync that changed its lanes (UTC).</param>
/// <param name="CatalogVersion">The catalog version at <c>progressAt</c>.</param>
/// <param name="Points">The synced event points at <c>progressAt</c>.</param>
/// <param name="Teams">The plan's teams at <c>progressAt</c>, by lane then <c>sortOrder</c>; empty when there was no
/// plan or no team.</param>
/// <param name="RosterSnapshot">The whole owned roster at <c>progressAt</c>; null when the snapshot was removed.</param>
public sealed record LegendaryEventRunRecordResponse(
    Guid Id,
    int Run,
    DateTimeOffset StartedAt,
    DateTimeOffset ProgressAt,
    string CatalogVersion,
    int Points,
    LegendaryEventRunRecordLanesResponse Lanes,
    IReadOnlyList<LegendaryEventRunRecordTeamResponse> Teams,
    RosterSnapshotResponse? RosterSnapshot
);

/// <summary>Each lane is null as synced, or its encounters in battle order.</summary>
public sealed record LegendaryEventRunRecordLanesResponse(
    IReadOnlyList<LegendaryEventRunRecordEncounterResponse>? Alpha,
    IReadOnlyList<LegendaryEventRunRecordEncounterResponse>? Beta,
    IReadOnlyList<LegendaryEventRunRecordEncounterResponse>? Gamma
);

/// <param name="ObjectivesCleared">As synced: 0 for defeat-all, 1–5 for the lane objectives.</param>
public sealed record LegendaryEventRunRecordEncounterResponse(IReadOnlyList<int> ObjectivesCleared, int HighScore);

/// <param name="MemberUnitIds">Line-up in position order.</param>
/// <param name="ObjectiveIndexes">Covered lane objective indexes, ascending.</param>
/// <param name="ExpectedBattleClears">The team's depth for the record's run, or null.</param>
/// <param name="ExpectedBattleClearsSource"><c>estimate</c> or <c>manual</c> when a depth is set.</param>
public sealed record LegendaryEventRunRecordTeamResponse(
    Guid TeamId,
    string LaneId,
    string Name,
    int SortOrder,
    IReadOnlyList<string> MemberUnitIds,
    string? ReserveUnitId,
    IReadOnlyList<int> ObjectiveIndexes,
    int? ExpectedBattleClears,
    string? ExpectedBattleClearsSource
);

/// <summary>A whole owned roster, units in the synced <c>characters</c> / <c>mows</c> chunk shapes.</summary>
/// <param name="TakenAt">When the units were read (UTC).</param>
public sealed record RosterSnapshotResponse(
    Guid Id,
    DateTimeOffset TakenAt,
    string CatalogVersion,
    IReadOnlyList<PlayerCharacterRecord> Characters,
    IReadOnlyList<PlayerMowRecord> Mows
);
