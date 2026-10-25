using System.Globalization;

namespace TacticusPlanner.Domain.Goals;

/// <summary>
/// The canonical identity of a goal's <em>end</em> target, for every goal type (<c>goal-target-milestones</c>).
/// Start/baseline, farming strategy, locations, sources and notes are never part of it, so two goals reaching
/// the same end state conflict however they got there. Null (Unlock, or a goal without its target group)
/// means one slot per unit and type. Shared by the in-flight slot check, target edits, conflict responses and
/// V1 import so they can't drift.
/// </summary>
public static class GoalTargetKey
{
    /// <summary>Written for an ability track whose end is not above its start: the goal leaves that track
    /// alone, so the level it happened to be at doesn't distinguish two goals.</summary>
    public const string NotAdvanced = "-";

    public static string? For(GoalType goalType, GoalConfig config) => goalType switch
    {
        GoalType.Rank => RankTargetKey.For(goalType, config),
        GoalType.Ascension => config.Progression is { } progression ? progression.End.Trim() : null,
        GoalType.Ability => config.Ability is { } ability ? Ability(ability) : null,
        GoalType.Upgrade => config.Upgrade is { } upgrade ? Upgrade(upgrade) : null,
        _ => null,
    };

    public static string? For(Goal goal) => For(goal.GoalType, goal.Config);

    private static string Ability(AbilityTarget target) =>
        $"{Track(target.ActiveStart, target.ActiveEnd)}:{Track(target.PassiveStart, target.PassiveEnd)}";

    private static string Track(int start, int end) =>
        end > start ? end.ToString(CultureInfo.InvariantCulture) : NotAdvanced;

    /// <summary>Targets sorted by upgrade id (ordinal) as <c>&lt;id&gt;x&lt;qty&gt;</c>, then each present
    /// range group as <c>|r</c>/<c>|a</c>/<c>|p&lt;start&gt;-&lt;end&gt;</c>.</summary>
    private static string Upgrade(UpgradeTarget target)
    {
        var materials = string.Join(',', target.Targets
            .OrderBy(item => item.UpgradeId.Trim(), StringComparer.Ordinal)
            .ThenBy(item => item.Quantity)
            .Select(item => string.Create(CultureInfo.InvariantCulture, $"{item.UpgradeId.Trim()}x{item.Quantity}")));
        return materials + Range('r', target.RankRange) + Range('a', target.ActiveRange) + Range('p', target.PassiveRange);
    }

    private static string Range(char group, UpgradeRange? range) =>
        range is null ? "" : string.Create(CultureInfo.InvariantCulture, $"|{group}{range.Start}-{range.End}");
}
