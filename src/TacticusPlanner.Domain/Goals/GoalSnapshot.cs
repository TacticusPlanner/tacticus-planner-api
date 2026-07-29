using TacticusPlanner.GameDomain;

namespace TacticusPlanner.Domain.Goals;

/// <summary>
/// The immutable calculation baseline frozen at goal creation — never refreshed or recalculated (plan
/// §10). Null until the estimation engine (a later phase) populates it at creation time. <c>CreatedAt</c>
/// was removed — it duplicated <see cref="Goal.CreatedAt"/> on the owning entity. The <c>Original*</c>
/// estimate fields and <c>InitialUnlocked</c> were removed as unused. <see cref="InitialRank"/>/
/// <see cref="InitialProgression"/> reuse <see cref="UnitRank"/>/<see cref="UnitProgression"/> — the same
/// enums the player-data layer already uses for this exact concept — rather than a second, duplicated pair.
/// </summary>
public sealed class GoalSnapshot
{
    public UnitRank? InitialRank { get; set; }

    public UnitProgression? InitialProgression { get; set; }

    public int? InitialActiveAbilityLevel { get; set; }

    public int? InitialPassiveAbilityLevel { get; set; }

    public List<GoalSnapshotResource> InitialRequirement { get; set; } = [];

    public List<GoalSnapshotResource> InitialInventoryContribution { get; set; } = [];
}

public sealed class GoalSnapshotResource
{
    public required string ResourceId { get; set; }

    public int Count { get; set; }
}
