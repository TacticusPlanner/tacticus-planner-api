namespace TacticusPlanner.GameCatalog.Models;

// One faction's characters and MoWs as read from the datamine lineup.json (source key units-{faction}; see
// Denormalization/UnitDatamineReader.cs). Name holds the faction id: display names are not catalog data.
public sealed record GameCatalogFactionUnits(
    string Alliance,
    string FactionId,
    string Name,
    IReadOnlyList<GameCatalogCharacter> Characters,
    IReadOnlyList<GameCatalogMow> Mows
);

public sealed record GameCatalogCharacter(
    string Id,
    string Name,
    int Health,
    int Damage,
    int Armour,
    string InitialRarity,
    string MeleeDamage,
    int MeleeHits,
    string? RangedDamage,
    int? RangedHits,
    int? RangeDistance,
    int Movement,
    IReadOnlyList<string> Traits,
    IReadOnlyList<string> ActiveAbilityDamage,
    IReadOnlyList<string> ActiveAbilityNames,
    IReadOnlyList<string> PassiveAbilityDamage,
    IReadOnlyList<string> PassiveAbilityNames,
    IReadOnlyList<string> EquipmentSlots,
    IReadOnlyList<GameCatalogCharacterRankUp> RankUpUpgrades
);

public sealed record GameCatalogCharacterRankUp(
    string Rank,
    IReadOnlyList<string> UpgradeIds
);

// A campaign-battle location that drops a reward (a character shard or an upgrade material), with the
// drop chance inlined. A single guaranteed reward carries Guaranteed=true and no rate; a single potential
// reward carries the resolved drop-chance numbers. Simultaneous occurrences of the same resource in one
// battle are consolidated: Guaranteed reflects whether an occurrence is guaranteed and EffectiveRate is
// their combined expected yield, while ChanceId/Numerator/Denominator remain null because no single chance
// definition represents the sum. IsMythic is true only for a character's mythicShards_ reward locations (see
// ShardPrefixes in GameCatalogDenormalizer.cs) — always false for upgrade-material locations, which have no
// mythic concept at all. ExpectedGold is a property of the battle, not of this specific resource: it's the
// average of the battle's own guaranteed gold reward (min+max)/2, null when the battle guarantees no gold —
// every location sharing that battle (even for a different resource) reports the same value.
public sealed record GameCatalogFarmLocation(
    string BattleId,
    string Type,
    bool Challenge,
    bool Guaranteed,
    string? ChanceId,
    int? Numerator,
    int? Denominator,
    double? EffectiveRate,
    bool IsMythic,
    double? ExpectedGold
);

public sealed record GameCatalogEquipmentSlot(
    string Slot,
    IReadOnlyList<string> EquipmentIds
);

public sealed record GameCatalogCharacterView(
    string Id,
    string Name,
    string Faction,
    string Alliance,
    int Health,
    int Damage,
    int Armour,
    string InitialRarity,
    string MeleeDamage,
    int MeleeHits,
    string? RangedDamage,
    int? RangedHits,
    int? RangeDistance,
    int Movement,
    IReadOnlyList<string> Traits,
    IReadOnlyList<string> ActiveAbilityDamage,
    IReadOnlyList<string> ActiveAbilityNames,
    IReadOnlyList<string> PassiveAbilityDamage,
    IReadOnlyList<string> PassiveAbilityNames,
    IReadOnlyList<string> EquipmentSlots,
    IReadOnlyList<GameCatalogCharacterRankUp> RankUpUpgrades,
    IReadOnlyList<GameCatalogFarmLocation> ShardLocations,
    IReadOnlyList<GameCatalogEquipmentSlot> EligibleEquipment,
    // none, optional or forced: see GameCatalogUnitSummons.
    string Summons
);

/// <summary>
/// The served <c>summons</c> of a character or MoW, curated in <c>unit-summons</c>: whether the unit can bring a
/// summon onto the battlefield, and whether the player can keep it from doing so.
/// </summary>
public static class GameCatalogUnitSummons
{
    /// <summary>No ability summons.</summary>
    public const string None = "none";

    /// <summary>
    /// The player decides whether it summons: an active ability left unused, or a passive the player can switch off
    /// (Tan Gi'da's needs his active on). An LRE "No Summons" battle allows it.
    /// </summary>
    public const string Optional = "optional";

    /// <summary>A passive ability summons on its own (on a kill, when attacked…), so the player cannot prevent it.</summary>
    public const string Forced = "forced";
}
