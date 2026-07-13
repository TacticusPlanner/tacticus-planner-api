namespace TacticusPlanner.Domain.Goals;

/// <summary>
/// The goal's target, as a flat superset of fields (mirrors V1's <c>IPersonalGoal</c> shape) — only the
/// fields relevant to the owning <see cref="Goal.EntityType"/>/<see cref="Goal.GoalType"/> pair are
/// populated. Immutable after creation (redefining a target means creating a replacement goal) — see the
/// plan's §7 "Immutable-after-creation policy".
/// </summary>
public sealed class GoalConfig
{
    // Rank goals.
    public int? RankStart { get; set; }
    public bool? RankStartPointFive { get; set; }
    public int? RankStartAppliedUpgrades { get; set; }
    public int? RankEnd { get; set; }
    public bool? RankEndPointFive { get; set; }
    public int? RankEndAppliedUpgrades { get; set; }

    // Ascension goals — a single progression selector value (see plan §5), not separate rarity/star pairs.
    public string? ProgressionStart { get; set; }
    public string? ProgressionEnd { get; set; }

    // Ability goals.
    public int? AbilityActiveStart { get; set; }
    public int? AbilityActiveEnd { get; set; }
    public int? AbilityPassiveStart { get; set; }
    public int? AbilityPassiveEnd { get; set; }

    // Shard/unlock goals.
    public int? ShardsTarget { get; set; }

    // Per-goal farming override (plan §6). Null/"auto" means the lowest-energy valid location is chosen
    // dynamically; the snapshot freezes requirements only, never the chosen location (plan §10).
    public string? FarmingMode { get; set; }
    public List<string>? FarmingLocationIds { get; set; }
}
