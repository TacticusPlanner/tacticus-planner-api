using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    public static IReadOnlyList<GameCatalogUpgradeView> BuildUpgrades(
        IReadOnlyDictionary<string, IReadOnlyList<GameCatalogUpgrade>> upgradesByRarity,
        IReadOnlyDictionary<string, GameCatalogCampaignGroup> campaignGroups,
        IReadOnlyList<GameCatalogDropChance> dropChances)
    {
        var dropChanceById = BuildDropChanceIndex(dropChances);
        var rewardLocations = BuildRewardLocations(campaignGroups);
        var upgrades = upgradesByRarity
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => pair.Value)
            .ToArray();
        var byId = upgrades.ToDictionary(upgrade => upgrade.Id, StringComparer.OrdinalIgnoreCase);

        var views = new List<GameCatalogUpgradeView>(upgrades.Length);
        foreach (var upgrade in upgrades)
        {
            views.Add(new GameCatalogUpgradeView(
                upgrade.Id,
                upgrade.Material,
                upgrade.SnowprintId,
                upgrade.Label,
                upgrade.Rarity,
                upgrade.Stat,
                upgrade.Icon,
                upgrade.Craftable,
                BuildNestedRecipe(upgrade.Recipe, byId, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { upgrade.Id }),
                ResolveLocations(upgrade.Id, rewardLocations, dropChanceById)));
        }

        return views;
    }

    // Builds a recipe tree: each ingredient that is itself a craftable upgrade carries its own nested
    // recipe (recursively); base materials have a null nested recipe. The stack guards against cycles.
    private static List<GameCatalogUpgradeRecipeIngredient> BuildNestedRecipe(
        IReadOnlyList<GameCatalogUpgradeRecipeIngredient> recipe,
        IReadOnlyDictionary<string, GameCatalogUpgrade> byId,
        HashSet<string> stack)
    {
        var ingredients = new List<GameCatalogUpgradeRecipeIngredient>(recipe.Count);
        foreach (var ingredient in recipe)
        {
            if (byId.TryGetValue(ingredient.Material, out var sub) && sub.Craftable && sub.Recipe.Count > 0
                && stack.Add(ingredient.Material))
            {
                var nested = BuildNestedRecipe(sub.Recipe, byId, stack);
                stack.Remove(ingredient.Material);
                ingredients.Add(new GameCatalogUpgradeRecipeIngredient(ingredient.Material, ingredient.Count, nested));
            }
            else
            {
                ingredients.Add(new GameCatalogUpgradeRecipeIngredient(ingredient.Material, ingredient.Count));
            }
        }

        return ingredients;
    }
}
