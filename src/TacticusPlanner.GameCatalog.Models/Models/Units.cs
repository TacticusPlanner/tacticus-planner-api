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
    // none, optional, conditional or forced: see GameCatalogUnitSummons.
    string Summons,
    // The active then passive abilities with their curated roles and target: see GameCatalogUnitAbility.
    IReadOnlyList<GameCatalogUnitAbility> Abilities
);

/// <summary>
/// One ability of a character or MoW as curated in <c>unit-abilities</c>: what it does (<c>Roles</c>, one or more of
/// <see cref="GameCatalogAbilityRoles"/>) and what it lands on (<c>Target</c>, one of <see cref="GameCatalogAbilityTargets"/>).
/// <c>Kind</c> is <c>active</c> or <c>passive</c> for a character and <c>primary</c> or <c>secondary</c> for a MoW.
/// <c>Attack</c> is <c>melee</c> or <c>ranged</c> when the datamine gives the ability that attack range, else null
/// (no attack, or the unit's normal attack).
/// </summary>
public sealed record GameCatalogUnitAbility(
    string Id,
    string Kind,
    IReadOnlyList<string> Roles,
    string Target,
    string? Attack
);

/// <summary>The roles an ability can have in <c>unit-abilities</c>.</summary>
public static class GameCatalogAbilityRoles
{
    /// <summary>The ability itself deals damage (counts as ability damage).</summary>
    public const string Damage = "damage";

    /// <summary>Heals, repairs, shields, revives or buffs the unit or its allies.</summary>
    public const string Support = "support";

    /// <summary>Summons, moves, marks, debuffs, taunts or changes terrain.</summary>
    public const string Utility = "utility";

    public static readonly IReadOnlySet<string> All = new HashSet<string>([Damage, Support, Utility], StringComparer.Ordinal);
}

/// <summary>What an ability in <c>unit-abilities</c> lands on.</summary>
public static class GameCatalogAbilityTargets
{
    /// <summary>No target to pick: the unit itself, or a trigger that hits nobody.</summary>
    public const string None = "none";

    /// <summary>One unit.</summary>
    public const string OneUnit = "single";

    /// <summary>An area or several units.</summary>
    public const string Aoe = "aoe";

    public static readonly IReadOnlySet<string> All = new HashSet<string>([None, OneUnit, Aoe], StringComparer.Ordinal);
}

/// <summary>
/// The served <c>summons</c> of a character or MoW, curated in <c>unit-summons</c>: whether the unit can bring a
/// summon onto the battlefield, and whether the player can keep it from doing so.
/// </summary>
public static class GameCatalogUnitSummons
{
    /// <summary>No ability summons.</summary>
    public const string None = "none";

    /// <summary>
    /// Only an active ability summons, so the player decides whether it summons. An LRE "No Summons" battle allows it.
    /// </summary>
    public const string Optional = "optional";

    /// <summary>
    /// A passive ability summons, but only while the player keeps a condition up that they can avoid (Tan Gi'da's
    /// needs his active switched on, Vynn's needs him to repair). An LRE "No Summons" battle allows it, though it is
    /// easy to summon by accident.
    /// </summary>
    public const string Conditional = "conditional";

    /// <summary>A passive ability summons on its own (on a kill, when attacked…), so the player cannot prevent it.</summary>
    public const string Forced = "forced";
}
