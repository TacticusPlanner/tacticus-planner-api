namespace TacticusPlanner.Domain.Goals;

/// <summary>
/// A stage within a single goal's own progression (e.g. one rank-group step toward a long-term rank
/// target). Never a separate goal record — see the plan's §11 "Milestone representation". Not populated by
/// any endpoint yet; the segmentation/estimation engine that generates these lands in a later phase.
/// </summary>
public sealed class GoalMilestone
{
    public int Index { get; set; }

    public required string Kind { get; set; }

    public required string TargetState { get; set; }

    /// <summary>"calculated" or "user".</summary>
    public required string Source { get; set; }

    public required string Status { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
