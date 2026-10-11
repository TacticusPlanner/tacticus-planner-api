using FastEndpoints;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

/// <summary>The one served plan shape, returned by every plan endpoint (design D8). Ids only: no unit,
/// objective or event display strings.</summary>
/// <param name="Teams">Ordered by lane (alpha, beta, gamma), then <c>sortOrder</c>.</param>
/// <param name="Runs">Run inputs, one entry per stored run, ascending by run; absent runs read as all zeros.</param>
/// <param name="Annotations">Maybe clear / Stop here marks, ordered by lane, then <c>battleIndex</c>, then
/// <c>objectiveId</c>. The client applies a mark only while synced progress shows its cell not cleared.</param>
public sealed record LegendaryEventPlanResponse(
    string EventId,
    long Revision,
    string CatalogVersion,
    string? Notes,
    bool ShowPaidOptions,
    IReadOnlyList<LegendaryEventTeamResponse> Teams,
    IReadOnlyList<LegendaryEventRunProgressResponse> Runs,
    IReadOnlyList<LegendaryEventObjectiveAnnotationResponse> Annotations
);

/// <param name="Run">The event run, 1–3.</param>
/// <param name="RegularMissions">0..the event's catalog <c>regularMissions</c> count.</param>
/// <param name="PremiumMissions">0..the event's catalog <c>premiumMissions</c> count.</param>
/// <param name="CloseShards">"Oh So Close" shards, 0–75.</param>
/// <param name="UpdatedAt">When this run's inputs were last written (UTC).</param>
public sealed record LegendaryEventRunProgressResponse(
    int Run,
    int RegularMissions,
    int PremiumMissions,
    bool BundlePurchased,
    int CloseShards,
    DateTimeOffset UpdatedAt
);

/// <param name="LaneId"><c>alpha</c>, <c>beta</c> or <c>gamma</c>.</param>
/// <param name="BattleIndex">0-based, as the synced encounters.</param>
/// <param name="ObjectiveId">0 for defeat-all, 1–5 for the lane objective whose catalog <c>index</c> is
/// <c>objectiveId − 1</c>.</param>
/// <param name="Status"><c>maybeClear</c> or <c>stopHere</c>.</param>
/// <param name="UpdatedAt">When this cell's mark was last written (UTC).</param>
public sealed record LegendaryEventObjectiveAnnotationResponse(
    string LaneId,
    int BattleIndex,
    int ObjectiveId,
    string Status,
    DateTimeOffset UpdatedAt
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

/// <param name="ExpectedRevision">The plan revision the caller last read (0 for a plan that does not exist).</param>
/// <param name="RegularMissions">0..the event's catalog <c>regularMissions</c> count.</param>
/// <param name="PremiumMissions">0..the event's catalog <c>premiumMissions</c> count.</param>
/// <param name="CloseShards">"Oh So Close" shards, 0–75.</param>
public sealed record UpdateLegendaryEventRunInputsRequest(
    long ExpectedRevision,
    int RegularMissions,
    int PremiumMissions,
    bool BundlePurchased,
    int CloseShards
);

/// <param name="ExpectedRevision">The plan revision the caller last read (0 for a plan that does not exist).</param>
/// <param name="BattleIndex">0-based battle index within the lane.</param>
/// <param name="ObjectiveIds">1–6 distinct cells of that battle: 0 for defeat-all, 1..the lane's objective
/// count for its objectives.</param>
/// <param name="Status"><c>maybeClear</c>, <c>stopHere</c>, or null to remove the listed cells' marks.</param>
public sealed record UpdateLegendaryEventAnnotationsRequest(
    long ExpectedRevision,
    string LaneId,
    int BattleIndex,
    IReadOnlyList<int> ObjectiveIds,
    string? Status
);
