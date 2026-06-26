using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    public static IReadOnlyList<GameCatalogCharacterView> BuildCharacters(
        IReadOnlyDictionary<string, GameCatalogFactionUnits> unitsByFaction,
        IReadOnlyDictionary<string, IReadOnlyList<GameCatalogEquipment>> equipmentByType,
        IReadOnlyDictionary<string, GameCatalogCampaignGroup> campaignGroups,
        IReadOnlyList<GameCatalogDropChance> dropChances)
    {
        var dropChanceById = BuildDropChanceIndex(dropChances);
        var rewardLocations = BuildRewardLocations(campaignGroups);
        var equipmentByTypeName = equipmentByType.Values
            .SelectMany(items => items)
            .GroupBy(item => item.Type, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var characters = new List<GameCatalogCharacterView>();
        foreach (var faction in unitsByFaction.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value))
        {
            foreach (var character in faction.Characters)
            {
                characters.Add(new GameCatalogCharacterView(
                    character.Id,
                    character.Name,
                    faction.FactionId,
                    faction.Alliance,
                    character.Health,
                    character.Damage,
                    character.Armour,
                    character.InitialRarity,
                    character.MeleeDamage,
                    character.MeleeHits,
                    character.RangedDamage,
                    character.RangedHits,
                    character.RangeDistance,
                    character.Movement,
                    character.Traits,
                    character.ActiveAbilityNames,
                    character.PassiveAbilityNames,
                    character.EquipmentSlots,
                    character.Icon,
                    character.RoundIcon,
                    character.RankUpUpgrades,
                    BuildShardLocations(character.Id, rewardLocations, dropChanceById),
                    BuildEligibleEquipment(character, faction.FactionId, equipmentByTypeName)));
            }
        }

        return characters;
    }

    private static List<GameCatalogFarmLocation> BuildShardLocations(
        string characterId,
        Dictionary<string, List<RewardLocation>> rewardLocations,
        Dictionary<string, GameCatalogDropChance> dropChanceById)
    {
        var result = new List<GameCatalogFarmLocation>();
        foreach (var prefix in ShardPrefixes)
        {
            foreach (var location in ResolveLocations(prefix + characterId, rewardLocations, dropChanceById))
            {
                result.Add(location);
            }
        }

        return result;
    }

    private static GameCatalogEquipmentSlot[] BuildEligibleEquipment(
        GameCatalogCharacter character,
        string factionId,
        Dictionary<string, GameCatalogEquipment[]> equipmentByTypeName) =>
        character.EquipmentSlots
            .Select(slot => new GameCatalogEquipmentSlot(
                slot,
                equipmentByTypeName.TryGetValue(slot, out var items)
                    ? items
                        .Where(item =>
                            item.AllowedFactions.Contains(factionId, StringComparer.Ordinal)
                            || item.AllowedUnits.Contains(character.Id, StringComparer.OrdinalIgnoreCase))
                        .Select(item => item.Id)
                        .ToArray()
                    : []))
            .ToArray();
}
