using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusApiPlayer = TacticusPlanner.TacticusApi.Models.Player;

namespace TacticusPlanner.Api.Features.PlayerData;

/// <summary>Character/MoW roster mapping half of <see cref="PlayerDataTransformer"/>.</summary>
public sealed partial class PlayerDataTransformer
{
    private static PlayerCharacterRecord MapCharacter(TacticusApiPlayer.Unit unit) => new()
    {
        UnitId = UnitId.From(unit.Id),
        ProgressionIndex = (UnitProgression)unit.ProgressionIndex,
        Xp = unit.Xp,
        XpLevel = unit.XpLevel,
        Rank = (UnitRank)unit.Rank,
        Shards = unit.Shards,
        MythicShards = unit.MythicShards,
        Abilities = MapAbilities(unit.Abilities),
        AppliedUpgradeSlots = (unit.Upgrades ?? []).ToList(),
        EquippedItems = (unit.Items ?? [])
            .Select(item => new PlayerUnitEquipmentSlotRecord
            {
                SlotId = item.SlotId,
                EquipmentId = item.Id,
                Level = item.Level,
            })
            .ToList(),
    };

    // MoWs have no rank and no equipment slots — see PlayerBaseUnitRecord/PlayerMowRecord.
    private static PlayerMowRecord MapMow(TacticusApiPlayer.Unit unit) => new()
    {
        UnitId = UnitId.From(unit.Id),
        ProgressionIndex = (UnitProgression)unit.ProgressionIndex,
        Xp = unit.Xp,
        XpLevel = unit.XpLevel,
        Shards = unit.Shards,
        MythicShards = unit.MythicShards,
        Abilities = MapAbilities(unit.Abilities),
        AppliedUpgradeSlots = (unit.Upgrades ?? []).ToList(),
    };

    private static List<PlayerUnitAbilityRecord> MapAbilities(IEnumerable<TacticusApiPlayer.Ability>? abilities) =>
        (abilities ?? [])
            .Select(ability => new PlayerUnitAbilityRecord { AbilityId = ability.Id, Level = ability.Level })
            .ToList();
}
