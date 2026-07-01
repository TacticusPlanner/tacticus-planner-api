using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    public static IReadOnlyList<GameCatalogEquipmentView> BuildEquipment(
        IReadOnlyDictionary<string, IReadOnlyList<GameCatalogEquipment>> equipmentByType,
        IReadOnlyList<GameCatalogEquipmentUpgradeCost> equipmentUpgradeCosts)
    {
        var levelsByRarity = equipmentUpgradeCosts
            .GroupBy(cost => cost.Rarity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Levels, StringComparer.Ordinal);

        return equipmentByType
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => pair.Value)
            .Select(item => new GameCatalogEquipmentView(
                item.Id,
                item.Name,
                item.Rarity,
                item.Type,
                item.AbilityId,
                item.IsRelic,
                item.IsUniqueRelic,
                item.AllowedUnits,
                item.AllowedFactions,
                item.Levels,
                levelsByRarity.TryGetValue(item.Rarity, out var levels) ? levels : []))
            .ToArray();
    }
}
