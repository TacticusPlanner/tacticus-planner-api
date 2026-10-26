using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>Raw path of the datamine file the upgrades dataset is built from.</summary>
    public const string DatamineUpgradesPath = "gameconfig/upgrades.json";

    /// <summary>Raw path of the datamine English strings, which carry the upgrades' display names.</summary>
    public const string DatamineEnglishPath = "languages/en.json";

    // The game's upgrade statType -> the served stat name.
    private static readonly Dictionary<string, string> UpgradeStatNames = new(StringComparer.Ordinal)
    {
        ["hp"] = "Health",
        ["dmg"] = "Damage",
        ["fixedArmor"] = "Armour",
    };

    /// <summary>
    /// Reads the datamine <c>upgrades.json</c> into upgrades grouped by source key
    /// (<c>upgrades-{rarity}</c> for base materials, <c>upgrades-{rarity}-crafted</c> for craftable ones), each
    /// group ordered by id. The label and material name are the English string <c>Upgrades/{id}_name</c>; the
    /// recipe is the <c>crafting</c> list (ingredient id and amount).
    /// </summary>
    /// <exception cref="InvalidOperationException">An upgrade has no English name or an unknown statType.</exception>
    public static IReadOnlyDictionary<string, IReadOnlyList<GameCatalogUpgrade>> ReadDatamineUpgrades(
        IReadOnlyDictionary<string, DatamineUpgrade> upgrades,
        IReadOnlyDictionary<string, string> english) =>
        upgrades
            .Select(pair =>
            {
                var (id, upgrade) = pair;
                if (!english.TryGetValue($"Upgrades/{id}_name", out var name)
                    || !UpgradeStatNames.TryGetValue(upgrade.StatType, out var stat))
                {
                    throw new InvalidOperationException(
                        $"Dataset '{GameCatalogDatasets.Upgrades}' upgrade '{id}' must have an English name " +
                        $"'Upgrades/{id}_name' and a statType of [{string.Join(", ", UpgradeStatNames.Keys)}]; " +
                        $"found statType '{upgrade.StatType}'.");
                }

                var recipe = (upgrade.Crafting ?? [])
                    .Select(ingredient => new GameCatalogUpgradeRecipeIngredient(ingredient.Id, ingredient.Amount))
                    .ToArray();
                return new GameCatalogUpgrade(id, name, id, name, upgrade.Rarity, stat, upgrade.Crafting is not null, recipe);
            })
            .GroupBy(upgrade => $"{GameCatalogDatasets.UpgradesPrefix}-{upgrade.Rarity.ToLowerInvariant()}{(upgrade.Craftable ? "-crafted" : "")}")
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<GameCatalogUpgrade>)group.OrderBy(upgrade => upgrade.Id, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

    public static IReadOnlyList<GameCatalogUpgradeView> BuildUpgrades(
        IReadOnlyDictionary<string, IReadOnlyList<GameCatalogUpgrade>> upgradesByRarity,
        IReadOnlyDictionary<string, GameCatalogCampaignGroup> campaignGroups,
        IReadOnlyList<GameCatalogDropChance> dropChances)
    {
        var dropChanceById = BuildDropChanceIndex(dropChances);
        var (rewardLocations, expectedGoldByBattle) = BuildRewardLocations(campaignGroups);
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
                upgrade.Craftable,
                BuildNestedRecipe(upgrade.Recipe, byId, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { upgrade.Id }),
                ResolveLocations(upgrade.Id, rewardLocations, dropChanceById, expectedGoldByBattle)));
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
