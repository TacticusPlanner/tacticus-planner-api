namespace TacticusPlanner.Domain.LegendaryEvents;

/// <summary>A Maybe clear / Stop here mark on one objective cell of a lane battle (LRE Stage 3). Cells are
/// identified as the synced progress identifies them; the client applies a mark only while sync shows the cell
/// not cleared, and sync never touches these marks.</summary>
public sealed class LegendaryEventObjectiveAnnotation
{
    /// <summary>One of <see cref="LegendaryEventValidation.LaneIds"/>.</summary>
    public required string LaneId { get; set; }

    /// <summary>0-based battle index, as the synced encounters.</summary>
    public int BattleIndex { get; set; }

    /// <summary>0 for defeat-all, 1–5 for the lane objective whose catalog <c>index</c> is
    /// <c>ObjectiveId − 1</c>.</summary>
    public int ObjectiveId { get; set; }

    public LegendaryEventAnnotationStatus Status { get; set; }

    /// <summary>When this cell's mark was last written.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum LegendaryEventAnnotationStatus
{
    MaybeClear,
    StopHere,
}
