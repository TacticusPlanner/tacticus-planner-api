using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    /// <summary>Each raw LRE file's reward ladder, served as <c>lres[].rewards</c>: points milestones numbered
    /// 1..n and strictly ascending by cumulative points, chest levels numbered 1..n, and every payout, cost,
    /// progression step and <c>shardsPerChest</c> positive.</summary>
    internal static void ValidateLreRewards(IEnumerable<GameCatalogLre> lres, List<GameCatalogValidationError> errors)
    {
        foreach (var lre in lres)
        {
            var problem = FindLadderProblem(lre);
            if (problem is not null)
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.LresPrefix,
                    "InvalidRewardLadder",
                    $"LRE '{lre.UnitSnowprintId}' ({lre.SourceFile}) has an invalid reward ladder: {problem}."));
            }
        }
    }

    private static string? FindLadderProblem(GameCatalogLre lre)
    {
        var points = lre.PointsMilestones;
        if (points.Count == 0)
            return "pointsMilestones is empty";
        for (var index = 0; index < points.Count; index++)
        {
            if (points[index].Milestone != index + 1)
                return $"pointsMilestones[{index}] is milestone {points[index].Milestone}, expected {index + 1}";
            if (index > 0 && points[index].CumulativePoints <= points[index - 1].CumulativePoints)
                return $"pointsMilestones[{index}] cumulativePoints is not ascending";
            if (points[index].CumulativePoints <= 0 || points[index].EngramPayout <= 0)
                return $"pointsMilestones[{index}] has a non-positive value";
        }

        var chests = lre.ChestsMilestones;
        if (chests.Count == 0)
            return "chestsMilestones is empty";
        for (var index = 0; index < chests.Count; index++)
        {
            if (chests[index].ChestLevel != index + 1)
                return $"chestsMilestones[{index}] is chest level {chests[index].ChestLevel}, expected {index + 1}";
            if (chests[index].EngramCost <= 0)
                return $"chestsMilestones[{index}] has a non-positive engramCost";
        }

        var progression = lre.Progression;
        int[] steps =
        [
            progression.Unlock, progression.FourStars, progression.FiveStars,
            progression.BlueStar, progression.Mythic, progression.TwoBlueStars,
        ];
        if (steps.Any(step => step <= 0))
            return "a progression step is not positive";

        return lre.ShardsPerChest <= 0 ? "shardsPerChest is not positive" : null;
    }
}
