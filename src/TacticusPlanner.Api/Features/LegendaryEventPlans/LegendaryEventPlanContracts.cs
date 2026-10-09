using TacticusPlanner.Domain.LegendaryEvents;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

/// <summary>The one served plan shape every plan endpoint answers with (design D8): the plan-level fields
/// and its teams ordered by lane (alpha, beta, gamma) then <c>sortOrder</c>. Ids only — no display
/// strings.</summary>
public sealed record LegendaryEventPlanResponse(
    string EventId,
    long Revision,
    string CatalogVersion,
    string? Notes,
    bool ShowPaidOptions,
    IReadOnlyList<LegendaryEventTeamResponse> Teams
);

/// <param name="MemberUnitIds">Positions 0..4 in order.</param>
/// <param name="ObjectiveIndexes">Ascending lane objective indexes.</param>
/// <param name="RunDepths">Ascending by run; a run with no stored depth is absent.</param>
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

/// <param name="ExpectedBattleClearsSource"><c>estimate</c> or <c>manual</c>.</param>
/// <param name="RecordedAt">ISO 8601 UTC time this run's depth was last written.</param>
public sealed record LegendaryEventTeamRunDepthResponse(
    int Run,
    int ExpectedBattleClears,
    string ExpectedBattleClearsSource,
    DateTimeOffset RecordedAt
);

/// <summary>409 body for every plan mutation: <c>legendaryEventPlanStale</c> when <c>expectedRevision</c>
/// no longer matches (or a concurrent writer won at save time), <c>legendaryEventOrderSetMismatch</c>
/// when a reorder does not name the lane's exact team set. Always carries the current plan so the client
/// reloads from the body without another round trip.</summary>
public sealed record LegendaryEventPlanConflictResponse(
    string IssueCode,
    string Message,
    LegendaryEventPlanResponse Plan
);

/// <summary>The wire names of <see cref="LegendaryEventDepthSource"/>.</summary>
public static class LegendaryEventDepthSources
{
    public const string Estimate = "estimate";
    public const string Manual = "manual";

    public static string ToWire(LegendaryEventDepthSource source) => source switch
    {
        LegendaryEventDepthSource.Estimate => Estimate,
        _ => Manual,
    };

    public static LegendaryEventDepthSource? FromWire(string? source) => source switch
    {
        Estimate => LegendaryEventDepthSource.Estimate,
        Manual => LegendaryEventDepthSource.Manual,
        _ => null,
    };
}

/// <summary>The validated, catalog-checked content of a team write, shared by create, update and the V1
/// import. Positions follow the order of <see cref="MemberUnitIds"/>.</summary>
public sealed record LegendaryEventTeamContent(
    string Name,
    IReadOnlyList<string> MemberUnitIds,
    string? ReserveUnitId,
    IReadOnlyList<int> ObjectiveIndexes
);

/// <summary>A clear-depth write for one run: a non-null depth upserts that run's row, a null one deletes
/// it; other runs are untouched (design D12).</summary>
public sealed record LegendaryEventRunDepthWrite(int Run, int? ExpectedBattleClears, LegendaryEventDepthSource? Source);
