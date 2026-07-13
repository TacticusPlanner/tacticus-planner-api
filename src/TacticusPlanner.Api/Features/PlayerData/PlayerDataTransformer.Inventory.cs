using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusApiPlayer = TacticusPlanner.TacticusApi.Models.Player;

namespace TacticusPlanner.Api.Features.PlayerData;

/// <summary>Inventory mapping half of <see cref="PlayerDataTransformer"/> — upgrades, items, shards,
/// and the remaining (unsplit) inventory categories.</summary>
public sealed partial class PlayerDataTransformer
{
    private static InventoryUpgradeRecord MapUpgrade(TacticusApiPlayer.Upgrade upgrade) => new()
    {
        UpgradeId = upgrade.Id,
        Amount = upgrade.Amount,
    };

    private static InventoryItemRecord MapItem(TacticusApiPlayer.InventoryEquipment item) => new()
    {
        ItemId = item.Id,
        Level = item.Level,
        Amount = item.Amount,
    };

    private static InventoryChunk MapInventory(TacticusApiPlayer.Inventory? inventory) => new()
    {
        XpBooks = (inventory?.XpBooks ?? [])
            .Select(book => new InventoryXpBookRecord { XpBookId = book.Id, Amount = book.Amount })
            .ToList(),
        AbilityBadges = new PlayerAbilityBadgesRecord
        {
            Imperial = (inventory?.AbilityBadges?.Imperial ?? []).Select(MapRarityAmount).ToList(),
            Xenos = (inventory?.AbilityBadges?.Xenos ?? []).Select(MapRarityAmount).ToList(),
            Chaos = (inventory?.AbilityBadges?.Chaos ?? []).Select(MapRarityAmount).ToList(),
        },
        Components = MapMowComponents(inventory?.Components),
        ForgeBadges = (inventory?.ForgeBadges ?? []).Select(MapRarityAmount).ToList(),
        Orbs = new PlayerOrbsRecord
        {
            Imperial = (inventory?.Orbs?.Imperial ?? []).Select(MapRarityAmount).ToList(),
            Xenos = (inventory?.Orbs?.Xenos ?? []).Select(MapRarityAmount).ToList(),
            Chaos = (inventory?.Orbs?.Chaos ?? []).Select(MapRarityAmount).ToList(),
        },
        RequisitionOrdersRegular = inventory?.RequisitionOrders?.Regular ?? 0,
        RequisitionOrdersBlessed = inventory?.RequisitionOrders?.Blessed ?? 0,
        ResetStones = inventory?.ResetStones ?? 0,
    };

    /// <summary>Merges the Tacticus API's two separate shard lists (regular/mythic) into one row per
    /// unit id — a unit can appear in only one of the two lists, so this unions both id sets rather
    /// than assuming every unit has both. Once a unit is unlocked (present in <c>Units</c>), its shard
    /// counts move onto the character/MoW record itself (<see cref="PlayerBaseUnitRecord.Shards"/> /
    /// <see cref="PlayerBaseUnitRecord.MythicShards"/>) instead, so this chunk only carries progress
    /// toward unlocking units the roster doesn't have yet — an already-unlocked id is dropped here to
    /// avoid keeping a second, stale copy of the same count in two chunks.</summary>
    private static List<InventoryShardRecord> MapShards(
        TacticusApiPlayer.Inventory? inventory,
        HashSet<string> unlockedUnitIds)
    {
        var regular = (inventory?.Shards ?? []).ToDictionary(shard => shard.Id, shard => shard.Amount);
        var mythic = (inventory?.MythicShards ?? []).ToDictionary(shard => shard.Id, shard => shard.Amount);

        return regular.Keys
            .Union(mythic.Keys, StringComparer.Ordinal)
            .Where(unitId => !unlockedUnitIds.Contains(unitId))
            .Select(unitId => new InventoryShardRecord
            {
                UnitId = UnitId.From(unitId),
                Amount = regular.GetValueOrDefault(unitId),
                MythicAmount = mythic.GetValueOrDefault(unitId),
            })
            .ToList();
    }

    private static PlayerRarityAmountRecord MapRarityAmount(TacticusApiPlayer.AbilityBadge badge) => new()
    {
        Rarity = badge.Rarity,
        Amount = badge.Amount,
    };

    private static PlayerRarityAmountRecord MapRarityAmount(TacticusApiPlayer.ForgeBadge badge) => new()
    {
        Rarity = badge.Rarity,
        Amount = badge.Amount,
    };

    private static PlayerRarityAmountRecord MapRarityAmount(TacticusApiPlayer.Orb orb) => new()
    {
        Rarity = orb.Rarity,
        Amount = orb.Amount,
    };

    // MoW components have no per-item identity worth tracking — just the total count per grand
    // alliance, mirroring how Orbs/AbilityBadges are already split.
    private static MowComponentsRecord MapMowComponents(IEnumerable<TacticusApiPlayer.MoWComponent>? components)
    {
        var list = (components ?? []).ToList();
        return new MowComponentsRecord
        {
            Imperial = new ComponentAmountRecord { Amount = list.Where(component => component.GrandAlliance == "Imperial").Sum(component => component.Amount) },
            Xenos = new ComponentAmountRecord { Amount = list.Where(component => component.GrandAlliance == "Xenos").Sum(component => component.Amount) },
            Chaos = new ComponentAmountRecord { Amount = list.Where(component => component.GrandAlliance == "Chaos").Sum(component => component.Amount) },
        };
    }
}
