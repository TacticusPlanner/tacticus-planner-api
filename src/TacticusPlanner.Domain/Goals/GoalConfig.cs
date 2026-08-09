namespace TacticusPlanner.Domain.Goals;

/// <summary>
/// The goal's target, grouped by goal kind so a whole group is null-or-fully-present rather than every
/// field independently nullable (mirrors V1's <c>IPersonalGoal</c> shape at the group level) — only the
/// group relevant to the owning <see cref="Goal.EntityType"/>/<see cref="Goal.GoalType"/> pair is
/// populated. Immutable after creation (redefining a target means creating a replacement goal) — see the
/// plan's §7 "Immutable-after-creation policy".
/// </summary>
public sealed class GoalConfig
{
    public RankTarget? Rank { get; set; }

    /// <summary>A single progression selector value (see plan §5), not separate rarity/star pairs.</summary>
    public ProgressionTarget? Progression { get; set; }

    public AbilityTarget? Ability { get; set; }

    public UpgradeTarget? Upgrade { get; set; }

    public LevelTarget? Level { get; set; }

    public FarmingStrategy FarmingStrategy { get; set; } = FarmingStrategy.TotalUpgrades;

    public AscensionFarmingConfig? AscensionFarming { get; set; }

    /// <summary>Per-goal farming location override (plan §6), as raw <see cref="CampaignBattleId"/> values
    /// — stored unwrapped since it's a scalar list nested in the <c>config</c> jsonb column (Vogen's
    /// cross-assembly EFCore discovery gap means custom-struct primitive collections aren't recognized
    /// there; the strong id is enforced at the API request boundary instead, see
    /// <c>CreateGoalConfigRequest</c>/<c>UpdateGoalRequest</c>). Null/empty means the lowest-energy valid
    /// location is chosen dynamically; the snapshot freezes requirements only, never the chosen location
    /// (plan §10).</summary>
    public List<string>? FarmingLocationIds { get; set; }
}

public sealed class RankTarget
{
    public required int Start { get; set; }
    public bool StartPointFive { get; set; }
    public int StartAppliedUpgrades { get; set; }
    public required int End { get; set; }
    public bool EndPointFive { get; set; }
    public int EndAppliedUpgrades { get; set; }
}

public sealed class ProgressionTarget
{
    public required string Start { get; set; }
    public required string End { get; set; }
}

public sealed class AbilityTarget
{
    public int ActiveStart { get; set; }
    public int ActiveEnd { get; set; }
    public int PassiveStart { get; set; }
    public int PassiveEnd { get; set; }
}

/// <summary>One or more specific upgrade materials, each with its own target quantity — the owning
/// unit's own Rank (Character) or Ability (Mow) requirements bound which upgrade ids are relevant
/// (enforced server-side, see <c>GoalTargetValidationService</c>).</summary>
public sealed class UpgradeTarget
{
    public List<UpgradeMaterialTarget> Targets { get; set; } = [];
}

public sealed class UpgradeMaterialTarget
{
    public required string UpgradeId { get; set; }
    public required int Quantity { get; set; }
}

/// <summary>Target character level (see <see cref="GoalEntityType.Character"/>). Uncosted — no
/// XP-cost curve/XP-book farming engine exists anywhere in this app yet; "complete" is simply the
/// unit's synced <c>XpLevel</c> reaching <see cref="End"/>.</summary>
public sealed class LevelTarget
{
    public required int Start { get; set; }
    public required int End { get; set; }
}

public enum FarmingStrategy
{
    TotalUpgrades,
    EveryStep,
    Milestones,
    MajorMilestones,
}

public enum AscensionFarmingSource
{
    Campaign,
    Onslaught,
    Both,
}

public sealed class AscensionFarmingConfig
{
    public AscensionFarmingSource Source { get; set; } = AscensionFarmingSource.Campaign;

    public List<string> ShardBattleIds { get; set; } = [];

    public List<string> MythicShardBattleIds { get; set; } = [];

}
