using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameDomain;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    // The ascension ladder must name exactly the progressions the planner knows, in the same order, so a game
    // change that adds, drops or reorders a step fails startup instead of shifting every cost by one step.
    internal static void ValidateAscensionCosts(
        IReadOnlyList<GameCatalogAscensionCost> costs, List<GameCatalogValidationError> errors)
    {
        const string dataset = GameCatalogDatasets.AscensionCostsServed;

        var progressions = costs.Select(cost => cost.Progression).ToArray();
        if (!progressions.SequenceEqual(ProgressionRules.ProgressionOrder))
        {
            errors.Add(new GameCatalogValidationError(
                dataset,
                "UnexpectedProgressions",
                $"Steps [{string.Join(", ", progressions)}] must be exactly [{string.Join(", ", ProgressionRules.ProgressionOrder)}]."));
        }

        foreach (var cost in costs.Where(cost => cost.Shards < 0 || cost.MythicShards < 0 || cost.Orbs < 0
            || (cost.Orbs > 0) != (cost.OrbRarity is not null)))
        {
            errors.Add(new GameCatalogValidationError(
                dataset, "InvalidCost", $"Step '{cost.Progression}' has a negative cost or orbs without a rarity."));
        }
    }
}
