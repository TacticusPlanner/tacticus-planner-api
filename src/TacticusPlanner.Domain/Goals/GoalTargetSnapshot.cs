namespace TacticusPlanner.Domain.Goals;

/// <summary>
/// The <em>end</em> target of one goal at a point in time, flattened to nullable scalars so a
/// <see cref="GoalEventType.TargetChanged"/> event can record "before" and "after" in the goal's JSON event
/// list without a shape per goal kind. Only the fields of the goal's own kind are set. Start/baseline is not
/// recorded — it never changes (see the creation <see cref="GoalSnapshot"/>).
/// </summary>
public sealed class GoalTargetSnapshot
{
    public int? RankEnd { get; set; }
    public bool? RankEndPointFive { get; set; }
    public int? RankEndAppliedUpgrades { get; set; }
    public string? ProgressionEnd { get; set; }
    public int? ActiveAbilityEnd { get; set; }
    public int? PassiveAbilityEnd { get; set; }
    public List<UpgradeMaterialTarget>? UpgradeTargets { get; set; }
    public UpgradeRange? UpgradeRankRange { get; set; }
    public UpgradeRange? UpgradeActiveRange { get; set; }
    public UpgradeRange? UpgradePassiveRange { get; set; }

    /// <summary>The current end target of <paramref name="config"/> for a goal of <paramref name="goalType"/>.</summary>
    public static GoalTargetSnapshot From(GoalType goalType, GoalConfig config) => goalType switch
    {
        GoalType.Rank when config.Rank is { } rank => new()
        {
            RankEnd = rank.End,
            RankEndPointFive = rank.EndPointFive,
            RankEndAppliedUpgrades = rank.EndAppliedUpgrades,
        },
        GoalType.Ascension when config.Progression is { } progression => new() { ProgressionEnd = progression.End },
        GoalType.Ability when config.Ability is { } ability => new()
        {
            ActiveAbilityEnd = ability.ActiveEnd,
            PassiveAbilityEnd = ability.PassiveEnd,
        },
        GoalType.Upgrade when config.Upgrade is { } upgrade => new()
        {
            UpgradeTargets = upgrade.Targets
                .Select(target => new UpgradeMaterialTarget { UpgradeId = target.UpgradeId, Quantity = target.Quantity })
                .ToList(),
            UpgradeRankRange = Copy(upgrade.RankRange),
            UpgradeActiveRange = Copy(upgrade.ActiveRange),
            UpgradePassiveRange = Copy(upgrade.PassiveRange),
        },
        _ => new(),
    };

    /// <summary>Same end target, ignoring upgrade-list order.</summary>
    public bool SameTargetAs(GoalTargetSnapshot other) =>
        RankEnd == other.RankEnd
        && RankEndPointFive == other.RankEndPointFive
        && RankEndAppliedUpgrades == other.RankEndAppliedUpgrades
        && ProgressionEnd == other.ProgressionEnd
        && ActiveAbilityEnd == other.ActiveAbilityEnd
        && PassiveAbilityEnd == other.PassiveAbilityEnd
        && UpgradeKey(UpgradeTargets) == UpgradeKey(other.UpgradeTargets)
        && SameRange(UpgradeRankRange, other.UpgradeRankRange)
        && SameRange(UpgradeActiveRange, other.UpgradeActiveRange)
        && SameRange(UpgradePassiveRange, other.UpgradePassiveRange);

    private static UpgradeRange? Copy(UpgradeRange? range) =>
        range is null ? null : new UpgradeRange { Start = range.Start, End = range.End };

    private static bool SameRange(UpgradeRange? a, UpgradeRange? b) =>
        a is null ? b is null : b is not null && a.Start == b.Start && a.End == b.End;

    private static string UpgradeKey(List<UpgradeMaterialTarget>? targets) =>
        targets is null
            ? string.Empty
            : string.Join('|', targets.OrderBy(target => target.UpgradeId, StringComparer.Ordinal)
                .Select(target => $"{target.UpgradeId}={target.Quantity}"));
}
