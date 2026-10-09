using FastEndpoints;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

/// <summary>The one served plan shape, returned by every plan endpoint (design D8). Ids only: no unit,
/// objective or event display strings.</summary>
/// <param name="Teams">Ordered by lane (alpha, beta, gamma), then <c>sortOrder</c>.</param>
public sealed record LegendaryEventPlanResponse(
    string EventId,
    long Revision,
    string CatalogVersion,
    string? Notes,
    bool ShowPaidOptions,
    IReadOnlyList<LegendaryEventTeamResponse> Teams
);

/// <param name="MemberUnitIds">Line-up in position order (1–5 units).</param>
/// <param name="ObjectiveIndexes">Covered lane objective indexes, ascending.</param>
/// <param name="RunDepths">One entry per stored run, ascending by run; empty when no depth is set.</param>
public sealed record LegendaryEventTeamResponse(
    Guid Id,
    string LaneId,
    string Name,
    int SortOrder,
    IReadOnlyList<string> MemberUnitIds,
    string? ReserveUnitId,
    IReadOnlyList<int> ObjectiveIndexes,
    IReadOnlyList<LegendaryEventTeamRunDepthResponse> RunDepths
);

/// <param name="ExpectedBattleClears">1..the lane's battle count.</param>
/// <param name="ExpectedBattleClearsSource"><c>estimate</c> or <c>manual</c>.</param>
/// <param name="RecordedAt">When this run's depth was last written (UTC).</param>
public sealed record LegendaryEventTeamRunDepthResponse(
    int Run,
    int ExpectedBattleClears,
    string ExpectedBattleClearsSource,
    DateTimeOffset RecordedAt
);

/// <summary>409 body: <c>legendaryEventPlanStale</c> (stale <c>expectedRevision</c> or a concurrent write) or
/// <c>legendaryEventOrderSetMismatch</c> (a reorder whose ids are not the lane's team set). Carries the current
/// plan so the client reloads from the body.</summary>
public sealed record LegendaryEventPlanConflictResponse(string IssueCode, string Message, LegendaryEventPlanResponse Plan);

public static class LegendaryEventPlanIssueCodes
{
    public const string Stale = "legendaryEventPlanStale";

    public const string OrderSetMismatch = "legendaryEventOrderSetMismatch";
}

/// <param name="ExpectedRevision">The plan revision the caller last read (0 for a plan that does not exist).</param>
public sealed record UpdateLegendaryEventPlanRequest(long ExpectedRevision, string? Notes, bool ShowPaidOptions);

/// <param name="Run">The event run (1–3) the depth applies to; other runs' depths are left as they are.</param>
/// <param name="ExpectedBattleClears">Clear depth for <paramref name="Run"/>; null deletes that run's depth.</param>
/// <param name="ExpectedBattleClearsSource"><c>estimate</c> or <c>manual</c>; null exactly when the depth is null.</param>
public sealed record CreateLegendaryEventTeamRequest(
    long ExpectedRevision,
    string LaneId,
    string Name,
    IReadOnlyList<string> MemberUnitIds,
    string? ReserveUnitId,
    IReadOnlyList<int> ObjectiveIndexes,
    int Run,
    int? ExpectedBattleClears,
    string? ExpectedBattleClearsSource
);

/// <summary>Same fields as a create minus the lane: a team never changes lane, so a body carrying
/// <paramref name="LaneId"/> is rejected with 400.</summary>
/// <param name="LaneId">Must be omitted.</param>
public sealed record UpdateLegendaryEventTeamRequest(
    long ExpectedRevision,
    string Name,
    IReadOnlyList<string> MemberUnitIds,
    string? ReserveUnitId,
    IReadOnlyList<int> ObjectiveIndexes,
    int Run,
    int? ExpectedBattleClears,
    string? ExpectedBattleClearsSource,
    string? LaneId = null
);

public sealed class DeleteLegendaryEventTeamRequest
{
    /// <summary>The plan revision the caller last read. Required.</summary>
    [QueryParam]
    public long? ExpectedRevision { get; init; }
}

/// <param name="TeamIds">The complete set of the lane's team ids, in the desired order.</param>
public sealed record UpdateLegendaryEventTeamOrderRequest(long ExpectedRevision, string LaneId, IReadOnlyList<Guid> TeamIds);
