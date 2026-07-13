namespace TacticusPlanner.Domain.Goals;

/// <summary>
/// The immutable calculation baseline frozen at goal creation — never refreshed or recalculated (plan
/// §10). Null until the estimation engine (a later phase) populates it at creation time; Phase 1 only
/// reserves the column and creates an empty baseline stamped with the creation time.
/// </summary>
public sealed class GoalSnapshot
{
    public DateTimeOffset CreatedAt { get; set; }

    public int? OriginalEstimateDays { get; set; }

    public DateTimeOffset? OriginalEstimateDate { get; set; }
}
