namespace TacticusPlanner.Domain.Goals;

/// <summary>
/// One append-only lifecycle event (plan §10) — gives active-vs-paused time accounting and priority
/// history without a separate table.
/// </summary>
public sealed class GoalEvent
{
    public DateTimeOffset At { get; set; }

    /// <summary>created | paused | resumed | priorityChanged | completed | archived | deleted.</summary>
    public required string Type { get; set; }
}
