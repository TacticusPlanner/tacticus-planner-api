namespace TacticusPlanner.GameCatalog.Models;

// ---- datamine shapes for units (bound from Data/datamine/raw/gameconfig/units/) ----------------------------
// Only the fields the catalog reads are bound; the rest (power multipliers, eligibility, visuals, ...) is dropped.

// One value of lineup.json, keyed by unit id: a character, or a MoW when traits contain `MachineOfWar`.
public sealed record DatamineLineupUnit(
    string Name,
    string FactionId,
    string GrandAllianceId,
    string BaseRarity,
    int? Movement,
    IReadOnlyList<DatamineWeapon>? Weapons,
    DatamineLineupStats Stats,
    IReadOnlyList<string> Traits,
    IReadOnlyList<string> ActiveAbilities,
    IReadOnlyList<string>? PassiveAbilities,
    IReadOnlyList<string>? MythicAbilities,
    IReadOnlyList<string>? ItemSlots,
    IReadOnlyList<IReadOnlyList<string>>? Upgrades
);

public sealed record DatamineLineupStats(int Health, int Damage, int FixedArmor);

// weapons[0] is the melee weapon; weapons[1], when present, is the ranged one and carries its Range.
public sealed record DatamineWeapon(int Hits, string DamageProfile, int? Range);

// One value of abilities.json, keyed by ability id (null for a few placeholder ids). `constants` holds the
// `damageProfile`, `damageProfile_2`, ... strings; `upgrades` is a MoW ability's per-level recipe list.
public sealed record DatamineAbility(
    IReadOnlyDictionary<string, string>? Constants,
    IReadOnlyList<IReadOnlyList<string>>? Upgrades
);

// One value of npc.json, keyed by NPC id. Loot objects have no FactionId / GrandAllianceId.
public sealed record DatamineNpc(
    string Name,
    string? FactionId,
    string? GrandAllianceId,
    int? Movement,
    IReadOnlyList<DatamineWeapon>? Weapons,
    IReadOnlyList<string>? Traits,
    IReadOnlyList<string>? ActiveAbilities,
    IReadOnlyList<string>? PassiveAbilities,
    IReadOnlyList<DatamineNpcStat> Stats
);

// One npc.json stat row; an absent number is 0.
public sealed record DatamineNpcStat(
    int Health,
    int Damage,
    int FixedArmor,
    int Rank,
    int StarLevel,
    int AbilityLevel,
    int ProgressionIndex
);
