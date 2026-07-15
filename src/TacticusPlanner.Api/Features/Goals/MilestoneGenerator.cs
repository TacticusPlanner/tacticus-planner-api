using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.PlayerData.Chunks;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Generates a Rank goal's <see cref="Goal.Milestones"/> at creation time (plan §11 — milestones are
/// generated once for the whole goal and stored in jsonb; nothing advances them yet, that's a later
/// phase). A pure port of V1's <c>bulk-goal-creator.service.ts</c> <c>"milestones"</c> breakpoint mode,
/// adapted to V2's 20-rank ladder: V1's set was
/// <c>[Bronze1, Silver1, Gold1, Diamond1, Diamond3, Adamantine3]</c>, but V2 has no <c>Adamantine3</c>
/// (nothing in-game can reach it yet — see <see cref="UnitRank"/>'s own comment), so the last breakpoint
/// is <c>Adamantine2</c>, the ladder's actual last reachable rank. Not user-configurable this phase — V1's
/// macro/milestones/full mode choice is dropped in favor of always using the "milestones" set.
/// </summary>
public static class MilestoneGenerator
{
    private static readonly string[] ProgressionOrder =
    [
        "Common:None", "Common:OneStar", "Common:TwoStars", "Uncommon:TwoStars",
        "Uncommon:ThreeStars", "Uncommon:FourStars", "Rare:FourStars", "Rare:FiveStars",
        "Rare:RedOneStar", "Epic:RedOneStar", "Epic:RedTwoStars", "Epic:RedThreeStars",
        "Legendary:RedThreeStars", "Legendary:RedFourStars", "Legendary:RedFiveStars",
        "Legendary:OneBlueStar", "Mythic:OneBlueStar", "Mythic:TwoBlueStars",
        "Mythic:ThreeBlueStars", "Mythic:MythicWings",
    ];

    private static readonly int[] Breakpoints =
    [
        (int)UnitRank.Bronze1,
        (int)UnitRank.Silver1,
        (int)UnitRank.Gold1,
        (int)UnitRank.Diamond1,
        (int)UnitRank.Diamond3,
        (int)UnitRank.Adamantine2,
    ];

    /// <summary>
    /// The milestone stages between a Rank goal's <c>start</c> (exclusive) and <c>end</c> (inclusive) —
    /// every breakpoint in that range, plus <c>end</c> itself if it isn't already one of them. Empty for
    /// an empty/inverted range (<c>end &lt;= start</c>).
    /// </summary>
    public static List<GoalMilestone> ForRank(int start, int end)
    {
        if (end <= start)
        {
            return [];
        }

        var stages = Breakpoints.Where(rank => rank > start && rank <= end).ToList();
        if (stages.Count == 0 || stages[^1] != end)
        {
            stages.Add(end);
        }

        return stages
            .Select((rank, index) => new GoalMilestone
            {
                Index = index,
                Kind = "rank",
                TargetState = ((UnitRank)rank).ToString(),
                Source = "calculated",
                Status = "pending",
            })
            .ToList();
    }

    public static List<GoalMilestone> ForProgression(string start, string end)
    {
        var startIndex = Array.IndexOf(ProgressionOrder, start);
        var endIndex = Array.IndexOf(ProgressionOrder, end);
        if (startIndex < 0 || endIndex <= startIndex)
        {
            return [];
        }

        return ProgressionOrder[(startIndex + 1)..(endIndex + 1)]
            .Select((target, index) => new GoalMilestone
            {
                Index = index,
                Kind = "progression",
                TargetState = target,
                Source = "calculated",
                Status = "pending",
            })
            .ToList();
    }
}
