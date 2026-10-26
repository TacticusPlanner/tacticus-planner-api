using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    internal static void ValidateCharacterAbilityCosts(
        IReadOnlyList<GameCatalogCharacterAbilityCost> costs, List<GameCatalogValidationError> errors)
    {
        const string dataset = GameCatalogDatasets.CharacterAbilityCostsServed;

        if (costs.Count == 0)
        {
            errors.Add(new GameCatalogValidationError(dataset, "EmptyDataset", $"Raw dataset '{dataset}' is empty."));
            return;
        }

        for (var index = 0; index < costs.Count; index++)
        {
            var cost = costs[index];
            if (cost.Lvl != index + 1)
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, "NonConsecutiveLevel", $"Entry {index} has lvl {cost.Lvl}; expected {index + 1}."));
            }

            if (cost.Badges is null)
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, "InvalidCost", $"Entry lvl {cost.Lvl} must have gold and exactly one abilityToken{{Rarity}} cost."));
            }
            else if (cost.Gold < 0 || cost.Badges.Amount <= 0 || !ValidRarities.Contains(cost.Badges.Rarity))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, "InvalidCost", $"Entry lvl {cost.Lvl} has negative gold, a non-positive badge amount or an unknown badge rarity."));
            }
        }
    }
}
