using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>Raw path of the datamine file the character-ability-costs dataset is built from.</summary>
    public const string DatamineAbilityUpgradeCostsPath = "gameconfig/units/abilityUpgradeCosts.json";

    private const string AbilityTokenPrefix = "abilityToken";

    /// <summary>
    /// Reads the datamine <c>abilityUpgradeCosts.json</c> (one row per ability level, each <c>gold</c> plus one
    /// <c>abilityToken{Rarity}</c> count) into cost rungs. Row N (1-based) is the cost from level N to N + 1.
    /// A row without <c>gold</c>, or with any key besides <c>gold</c> and exactly one <c>abilityToken*</c>, gets null
    /// badges, which validation rejects.
    /// </summary>
    public static IReadOnlyList<GameCatalogCharacterAbilityCost> ReadDatamineCharacterAbilityCosts(
        IReadOnlyList<IReadOnlyDictionary<string, int>> rows) =>
        rows
            .Select((row, index) =>
            {
                var hasGold = row.TryGetValue("gold", out var gold);
                var badgeKeys = row.Keys.Where(key => key != "gold").ToList();
                var badges = hasGold && badgeKeys is [var key] && key.StartsWith(AbilityTokenPrefix, StringComparison.Ordinal)
                    ? new GameCatalogAmountByRarity(key[AbilityTokenPrefix.Length..], row[key])
                    : null;
                return new GameCatalogCharacterAbilityCost(index + 1, gold, badges);
            })
            .ToArray();

    // Raw lvl N is the cost to go from level N to N + 1, so the served level is lvl + 1.
    public static IReadOnlyList<GameCatalogCharacterAbilityCostView> BuildCharacterAbilityCosts(
        IReadOnlyList<GameCatalogCharacterAbilityCost> costs) =>
        costs
            .Select(cost => new GameCatalogCharacterAbilityCostView(cost.Lvl + 1, cost.Gold, cost.Badges!))
            .ToArray();
}
