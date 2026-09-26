using TacticusPlanner.GameDomain;

namespace TacticusPlanner.Domain.Goals;

/// <summary>
/// The canonical identity of a Rank goal's <em>end</em> target: end rank plus the normalized applied-slot
/// count (<c>"&lt;rank&gt;:&lt;slots&gt;"</c>). Start/baseline and farming strategy are deliberately not part of
/// it, so two goals reaching the same end state conflict however they got there. Shared by request
/// validation, project membership (<c>ProjectGoal.RankTargetKey</c>) and V1 import so they can't drift.
/// </summary>
public static class RankTargetKey
{
    /// <summary>Below Adamantine1 a rank's upgrades are a 3-slot row and point-five means all 3, so
    /// (pointFive, 0) and (false, 3) are one end state; at Adamantine1+ slots are numbered individually and
    /// point-five carries no meaning.</summary>
    public static string From(int end, bool endPointFive, int endAppliedUpgrades)
    {
        var slots = Math.Max(endAppliedUpgrades, 0);
        if (end < (int)UnitRank.Adamantine1)
            slots = endPointFive ? 3 : Math.Min(slots, 3);
        return $"{end}:{slots}";
    }

    public static string From(RankTarget target) => From(target.End, target.EndPointFive, target.EndAppliedUpgrades);

    /// <summary>The key a goal's membership carries: non-null only for a Rank goal with a target.</summary>
    public static string? For(GoalType goalType, GoalConfig config) =>
        goalType == GoalType.Rank && config.Rank is { } rank ? From(rank) : null;
}
