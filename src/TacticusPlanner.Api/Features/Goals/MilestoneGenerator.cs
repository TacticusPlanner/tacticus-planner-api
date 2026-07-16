using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.PlayerData.Chunks;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Generates a Rank goal's <see cref="Goal.Milestones"/> at creation time (plan §11 — milestones are
/// generated once for the whole goal and stored in jsonb. Breakpoint strategies preserve V1's rank
/// segmentation and provide the approved rarity-tier analogue for Machine of War abilities.
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

    private static readonly int[] MilestoneBreakpoints =
    [
        (int)UnitRank.Bronze1,
        (int)UnitRank.Silver1,
        (int)UnitRank.Gold1,
        (int)UnitRank.Diamond1,
        (int)UnitRank.Diamond3,
        (int)UnitRank.Adamantine3,
    ];

    private static readonly int[] MajorBreakpoints =
        [(int)UnitRank.Gold1, (int)UnitRank.Diamond3, (int)UnitRank.Adamantine3];

    private static readonly int[] AbilityMilestoneBreakpoints = [17, 26, 35, 50, 60];
    private static readonly int[] AbilityMajorBreakpoints = [35, 50, 60];

    /// <summary>
    /// The milestone stages between a Rank goal's <c>start</c> (exclusive) and <c>end</c> (inclusive) —
    /// every breakpoint in that range, plus <c>end</c> itself if it isn't already one of them. Empty for
    /// an empty/inverted range (<c>end &lt;= start</c>).
    /// </summary>
    public static List<GoalMilestone> ForRank(
        int start,
        int end,
        FarmingStrategy strategy = FarmingStrategy.Milestones)
    {
        if (end <= start)
        {
            return [];
        }

        var breakpoints = strategy switch
        {
            FarmingStrategy.TotalUpgrades => [],
            FarmingStrategy.EveryStep => Enumerable.Range(start + 1, end - start).ToArray(),
            FarmingStrategy.MajorMilestones => MajorBreakpoints,
            _ => MilestoneBreakpoints,
        };
        var stages = breakpoints.Where(rank => rank > start && rank <= end).ToList();
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

    public static List<GoalMilestone> ForAbility(int start, int end, FarmingStrategy strategy)
    {
        if (end <= start) return [];
        var breakpoints = strategy switch
        {
            FarmingStrategy.TotalUpgrades => [],
            FarmingStrategy.EveryStep => Enumerable.Range(start + 1, end - start).ToArray(),
            FarmingStrategy.MajorMilestones => AbilityMajorBreakpoints,
            _ => AbilityMilestoneBreakpoints,
        };
        var stages = breakpoints.Where(level => level > start && level <= end).ToList();
        if (stages.Count == 0 || stages[^1] != end) stages.Add(end);
        return stages.Select((level, index) => new GoalMilestone
        {
            Index = index,
            Kind = "ability",
            TargetState = level.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Source = "calculated",
            Status = "pending",
        }).ToList();
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
