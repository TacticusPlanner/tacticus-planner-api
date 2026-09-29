using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    // Raw lvl N is the cost to go from level N to N + 1, so the served level is lvl + 1.
    public static IReadOnlyList<GameCatalogCharacterAbilityCostView> BuildCharacterAbilityCosts(
        IReadOnlyList<GameCatalogCharacterAbilityCost> costs) =>
        costs
            .Select(cost => new GameCatalogCharacterAbilityCostView(
                cost.Lvl + 1,
                cost.Gold,
                new GameCatalogAmountByRarity(CharacterAbilityBadgeRarity(cost.Lvl + 1), cost.Badges)))
            .ToArray();

    // Badge rarity bands by the level raised to, ported from V1's CharactersAbilitiesService.getRarityFromLevel
    // (not game data; the only source we have).
    internal static string CharacterAbilityBadgeRarity(int level) => level switch
    {
        <= 8 => "Common",
        <= 17 => "Uncommon",
        <= 26 => "Rare",
        <= 35 => "Epic",
        <= 50 => "Legendary",
        _ => "Mythic",
    };
}
