using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.Domain.RosterSnapshots;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Features.RosterSnapshots;

/// <summary>
/// The one copy routine every roster owner uses (<c>roster-snapshots</c>): Legendary Event run records and
/// personal snapshots, so an equal synced roster yields an equal snapshot in either owner.
/// </summary>
public static class RosterSnapshotFactory
{
    /// <summary>A full copy of the roster, units ordered by id, stamped with <paramref name="takenAt"/> and the
    /// current catalog version.</summary>
    public static RosterSnapshot Create(
        IReadOnlyList<PlayerCharacterRecord> characters, IReadOnlyList<PlayerMowRecord> mows, DateTimeOffset takenAt) =>
        new()
        {
            TakenAt = takenAt,
            CatalogVersion = GameCatalogRelease.Version,
            Characters = characters.OrderBy(unit => unit.UnitId.Value, StringComparer.Ordinal).Select(CloneCharacter).ToList(),
            Mows = mows.OrderBy(unit => unit.UnitId.Value, StringComparer.Ordinal).Select(CloneMow).ToList(),
        };

    // The synced chunk instances may also be owned by the player-data snapshot in this same unit of work, and EF
    // tracks an owned instance under one owner only, so every roster gets its own copies.
    private static PlayerCharacterRecord CloneCharacter(PlayerCharacterRecord unit) =>
        new()
        {
            UnitId = unit.UnitId,
            ProgressionIndex = unit.ProgressionIndex,
            Xp = unit.Xp,
            XpLevel = unit.XpLevel,
            Shards = unit.Shards,
            MythicShards = unit.MythicShards,
            Abilities = unit.Abilities.Select(CloneAbility).ToList(),
            AppliedUpgradeSlots = [.. unit.AppliedUpgradeSlots],
            Rank = unit.Rank,
            EquippedItems = unit.EquippedItems
                .Select(item => new PlayerUnitEquipmentSlotRecord
                {
                    SlotId = item.SlotId,
                    EquipmentId = item.EquipmentId,
                    Level = item.Level,
                })
                .ToList(),
        };

    private static PlayerMowRecord CloneMow(PlayerMowRecord unit) =>
        new()
        {
            UnitId = unit.UnitId,
            ProgressionIndex = unit.ProgressionIndex,
            Xp = unit.Xp,
            XpLevel = unit.XpLevel,
            Shards = unit.Shards,
            MythicShards = unit.MythicShards,
            Abilities = unit.Abilities.Select(CloneAbility).ToList(),
            AppliedUpgradeSlots = [.. unit.AppliedUpgradeSlots],
        };

    private static PlayerUnitAbilityRecord CloneAbility(PlayerUnitAbilityRecord ability) =>
        new() { AbilityId = ability.AbilityId, Level = ability.Level };
}
