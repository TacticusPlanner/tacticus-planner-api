using System.Text.Json.Serialization;

namespace TacticusPlanner.Domain.LegendaryEvents;

/// <summary>A team on one lane of a <see cref="LegendaryEventPlan"/>, stored inline in the plan's <c>teams</c>
/// jsonb. Run records copy the whole team as it was at their last progress, so one shape serves both.
/// <see cref="SortOrder"/> is dense and 0-based within the lane.</summary>
public sealed class LegendaryEventTeamRecord
{
    public LegendaryEventTeamId Id { get; set; }

    /// <summary>The catalog track key: one of <see cref="LegendaryEventValidation.LaneIds"/>.</summary>
    public string LaneId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    /// <summary>The line-up in position order (the index is the position, 0–4).</summary>
    public List<string> MemberUnitIds { get; set; } = [];

    public string? ReserveUnitId { get; set; }

    /// <summary>The lane objectives the team covers, by the catalog's 0-based objective <c>index</c>, ascending.</summary>
    public List<int> ObjectiveIndexes { get; set; } = [];

    /// <summary>The team's expected clear depth per run, at most one per run.</summary>
    public List<LegendaryEventTeamRunDepth> RunDepths { get; set; } = [];

    /// <summary>A detached copy: an owned instance can belong to one owner only.</summary>
    public LegendaryEventTeamRecord Clone() =>
        new()
        {
            Id = Id,
            LaneId = LaneId,
            Name = Name,
            SortOrder = SortOrder,
            MemberUnitIds = [.. MemberUnitIds],
            ReserveUnitId = ReserveUnitId,
            ObjectiveIndexes = [.. ObjectiveIndexes],
            RunDepths = RunDepths
                .Select(depth => new LegendaryEventTeamRunDepth
                {
                    Run = depth.Run,
                    ExpectedBattleClears = depth.ExpectedBattleClears,
                    Source = depth.Source,
                    RecordedAt = depth.RecordedAt,
                })
                .ToList(),
        };
}

/// <summary>The team's expected clear depth for one run (1–3) of the event.</summary>
public sealed class LegendaryEventTeamRunDepth
{
    public int Run { get; set; }

    public int ExpectedBattleClears { get; set; }

    public LegendaryEventDepthSource Source { get; set; }

    /// <summary>When this run's depth was last written.</summary>
    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>Wire names are camelCase (<c>estimate</c>/<c>manual</c>); storage converts by member name.</summary>
public enum LegendaryEventDepthSource
{
    [JsonStringEnumMemberName("estimate")] Estimate,
    [JsonStringEnumMemberName("manual")] Manual,
}
