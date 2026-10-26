using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>Raw path of the datamine file the equipment dataset is built from.</summary>
    public const string DatamineItemsPath = "gameconfig/items.json";

    // The game's stat names, mapped to the names the equipment dataset has always served. Keys not listed
    // (critChance, blockChance, hp, the *ChanceBonus keys) are served under the game's own name.
    private static readonly Dictionary<string, string> ServedStatNames = new(StringComparer.Ordinal)
    {
        ["critDmg"] = "critDamage",
        ["critDmgBonus"] = "critDamageBonus",
        ["blockDmg"] = "blockDamage",
        ["blockDmgBonus"] = "blockDamageBonus",
        ["fixedArmor"] = "armor",
    };

    /// <summary>
    /// Reads the datamine <c>items.json</c> (keyed by item id) into catalog equipment grouped by item type, each
    /// type's items ordered by id (ordinal). Each level's stat names are mapped to the served names, and its
    /// <c>goldCost</c> / <c>dustCost</c> / <c>mythicDustCost</c> (absent = 0) become the item's upgrade ladder.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<GameCatalogEquipment>> ReadDatamineEquipment(
        IReadOnlyDictionary<string, DatamineItem> items) =>
        items
            .Select(pair => new GameCatalogEquipment(
                pair.Key,
                pair.Value.Name,
                pair.Value.Rarity,
                pair.Value.ItemType,
                pair.Value.AbilityId ?? string.Empty,
                pair.Value.IsRelic ?? false,
                pair.Value.IsUniqueRelic ?? false,
                pair.Value.AllowedUnits ?? [],
                pair.Value.AllowedFactions ?? [],
                pair.Value.Levels
                    .Select(level => new GameCatalogEquipmentLevel(level.Stats.ToDictionary(
                        stat => ServedStatNames.GetValueOrDefault(stat.Key, stat.Key),
                        stat => stat.Value,
                        StringComparer.Ordinal)))
                    .ToArray(),
                pair.Value.Levels
                    .Select(level => new GameCatalogEquipmentUpgradeLevel(
                        level.GoldCost ?? 0,
                        level.DustCost ?? 0,
                        level.MythicDustCost ?? 0))
                    .ToArray()))
            .GroupBy(item => item.Type, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<GameCatalogEquipment>)group.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

    public static IReadOnlyList<GameCatalogEquipmentView> BuildEquipment(
        IReadOnlyDictionary<string, IReadOnlyList<GameCatalogEquipment>> equipmentByType) =>
        equipmentByType
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
                item.UpgradeLevels))
            .ToArray();
}
