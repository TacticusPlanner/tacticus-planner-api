namespace TacticusPlanner.Domain.PlayerData.Chunks;

/// <summary>Shared fields for a character or a MoW. MoWs have no rank and no equipment slots, so
/// those live only on <see cref="PlayerCharacterRecord"/> — see <see cref="PlayerMowRecord"/>.</summary>
public abstract class PlayerBaseUnitRecord
{
    /// <summary>Catalog/Tacticus unit id (stable across both systems; no realignment needed here).</summary>
    public UnitId UnitId { get; set; } = UnitId.From(string.Empty);

    public UnitProgression ProgressionIndex { get; set; }

    public long Xp { get; set; }

    public int XpLevel { get; set; }

    public long Shards { get; set; }

    public long MythicShards { get; set; }

    /// <summary>
    /// Raw ability id/level pairs. Categorizing an ability as active/passive (character) or
    /// primary/secondary (MoW) requires catalog ability metadata and is left to read-time
    /// projection rather than baked into the stored record.
    /// </summary>
    public List<PlayerUnitAbilityRecord> Abilities { get; set; } = [];

    /// <summary>Applied upgrade slot indices (2x3 matrix positions), as returned by the API.</summary>
    public List<int> AppliedUpgradeSlots { get; set; } = [];
}

public sealed class PlayerCharacterRecord : PlayerBaseUnitRecord
{
    public UnitRank Rank { get; set; }

    public List<PlayerUnitEquipmentSlotRecord> EquippedItems { get; set; } = [];
}

public sealed class PlayerMowRecord : PlayerBaseUnitRecord;

public sealed class PlayerUnitAbilityRecord
{
    public string AbilityId { get; set; } = string.Empty;

    public int Level { get; set; }
}

public sealed class PlayerUnitEquipmentSlotRecord
{
    public string SlotId { get; set; } = string.Empty;

    public string EquipmentId { get; set; } = string.Empty;

    public int Level { get; set; }
}
