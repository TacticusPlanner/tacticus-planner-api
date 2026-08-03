namespace TacticusPlanner.GameCatalog.Models;

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
// battle are consolidated: Guaranteed reflects whether one occurrence is guaranteed and EffectiveRate is
// their combined expected yield, while Numerator/Denominator remain null because no single fraction
// represents the sum. IsMythic is true only for a character's mythicShards_ reward locations (see
// ShardPrefixes in GameCatalogDenormalizer.cs) — always false for upgrade-material locations, which have no
// mythic concept at all.
public sealed record GameCatalogFarmLocation(
    string BattleId,
    string Type,
    bool Challenge,
    bool Guaranteed,
    string? ChanceId,
    int? Numerator,
    int? Denominator,
    double? EffectiveRate,
    bool IsMythic
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
    IReadOnlyList<GameCatalogEquipmentSlot> EligibleEquipment
);
