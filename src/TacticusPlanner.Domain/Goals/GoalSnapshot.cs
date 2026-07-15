namespace TacticusPlanner.Domain.Goals;

/// <summary>
/// The immutable calculation baseline frozen at goal creation — never refreshed or recalculated (plan
/// §10). Null until the estimation engine (a later phase) populates it at creation time; Phase 1 only
/// reserves the column and creates an empty baseline stamped with the creation time.
/// </summary>
public sealed class GoalSnapshot
{
    public DateTimeOffset CreatedAt { get; set; }

    public string? InitialRank { get; set; }

    public string? InitialProgression { get; set; }

    public int? InitialActiveAbilityLevel { get; set; }

    public int? InitialPassiveAbilityLevel { get; set; }

    public int? InitialShards { get; set; }

    public bool? InitialUnlocked { get; set; }

    public List<GoalSnapshotResource> InitialRequirement { get; set; } = [];

    public List<GoalSnapshotResource> InitialInventoryContribution { get; set; } = [];

    public int? OriginalEnergyTotal { get; set; }

    public int? OriginalRaidsTotal { get; set; }

    public int? OriginalEstimateDays { get; set; }

    public DateTimeOffset? OriginalEstimateDate { get; set; }
}

public sealed class GoalSnapshotResource
{
    public required string ResourceId { get; set; }

    public int Count { get; set; }
}
