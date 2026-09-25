namespace TacticusPlanner.Domain.Goals;

/// <summary>
/// One append-only lifecycle event (plan §10) — gives active-vs-paused time accounting and priority
/// history without a separate table.
/// </summary>
public sealed class GoalEvent
{
    public DateTimeOffset At { get; set; }

    public required GoalEventType Type { get; set; }

    /// <summary>The end target before/after a <see cref="GoalEventType.TargetChanged"/>; null for every other
    /// event, and for events written before target editing existed.</summary>
    public GoalTargetSnapshot? PreviousTarget { get; set; }

    public GoalTargetSnapshot? NewTarget { get; set; }
}
