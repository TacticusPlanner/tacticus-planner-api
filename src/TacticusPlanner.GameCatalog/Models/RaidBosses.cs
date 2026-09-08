using System.Text.Json.Serialization;

namespace TacticusPlanner.GameCatalog.Models;

// ---- raw authored shape (internal to denormalization; bound from Data/raid-bosses/raid-boss-data.json) --
//
// Ported from V1's datamined guild_boss.json (see scripts/port-raid-boss-data.mjs). PascalCase field names
// are bound case-insensitively by the loader. Numeric stat fields are non-nullable (a missing source value
// is written as 0 by the port script); genuinely-optional fields stay nullable.

/// <summary>
/// The whole raid-boss raw source: the season-config rotation, the primarch prime ids, the keyed unit
/// sets, the keyed season configs, and the keyed modifier definitions. Consolidated into the served
/// <see cref="GameCatalogRaidBossesView"/> by <c>Denormalization/RaidBossDenormalizer.cs</c>.
/// </summary>
public sealed record GameCatalogRaidBossRawData(
    IReadOnlyList<string> Rotation,
    IReadOnlyList<string> Primarchs,
    IReadOnlyDictionary<string, GameCatalogRaidBossRawUnitSet> UnitSets,
    IReadOnlyDictionary<string, GameCatalogRaidBossRawSeason> Seasons,
    IReadOnlyDictionary<string, GameCatalogRaidBossRawModifier> Modifiers);

public sealed record GameCatalogRaidBossRawUnitSet(
    string FactionId,
    int Movement,
    int? NrOfMembers,
    string? QuestUnitId,
    IReadOnlyList<GameCatalogRaidBossRawStat> Stats,
    IReadOnlyList<GameCatalogRaidBossRawWeapon>? Weapons,
    IReadOnlyList<string>? ActiveAbilities,
    IReadOnlyList<string>? PassiveAbilities,
    IReadOnlyList<string>? RelicAbilities,
    IReadOnlyList<string>? Traits);

public sealed record GameCatalogRaidBossRawStat(
    int Health,
    int Damage,
    int FixedArmor,
    int Rank,
    int StarLevel,
    string BaseRarity,
    int ProgressionIndex,
    int AbilityLevel,
    int? RelicAbilityLevel,
    double? BlockChance,
    double? BlockDamage,
    double? CritChance,
    double? CritDamage);

public sealed record GameCatalogRaidBossRawWeapon(int Hits, string DamageProfile, double? Range);

public sealed record GameCatalogRaidBossRawSeason(
    string GuildBossSeasonConfigId,
    int? LoopFromTier,
    int? LoopFromSet,
    IReadOnlyList<GameCatalogRaidBossRawTier> Tiers);

public sealed record GameCatalogRaidBossRawTier(int Tier, IReadOnlyList<GameCatalogRaidBossRawSet> Sets);

public sealed record GameCatalogRaidBossRawSet(
    int Set,
    string ChestId,
    int GuildXp,
    IReadOnlyList<GameCatalogRaidBossRawEncounter> Encounters);

public sealed record GameCatalogRaidBossRawEncounter(
    int EncounterIndex,
    string GuildBossEncounterType,
    string BoardId,
    int MaxNrOfTurns,
    string? BossType,
    string UnitId,
    string? Npc1Id,
    string? Npc2Id,
    IReadOnlyList<string>? Enemies,
    IReadOnlyList<string>? DisallowedFactions,
    IReadOnlyList<GameCatalogRaidBossRawEncounterModifier>? Modifiers);

public sealed record GameCatalogRaidBossRawEncounterModifier(double HpLost, string Modifier);

/// <summary>A raw modifier definition: <c>subtarget</c> is present only for some modifier types.</summary>
public sealed record GameCatalogRaidBossRawModifier(string Type, string Target, string? Subtarget, double Amount);

// ---- served view (public catalog surface) --------------------------------------------------------
//
// Structural / identity fields only — no display name, short name, portrait/icon path, icon id, or wiki
// link anywhere. The client resolves every boss/prime/ability/trait/faction/npc name and image from its
// id. See specs/raid-bosses-dataset in the raid-bosses-library change.

/// <summary>
/// The consolidated <c>raid-bosses</c> dataset: the season-config rotation order, the raid bosses and
/// raid-boss primes as two ordered lists, and the season configs keyed by id. Encounters carry their
/// referenced unit-set id, progression index, field-npc ids, and modifier definitions inlined, so the
/// client never joins one collection against another.
/// </summary>
public sealed record GameCatalogRaidBossesView(
    IReadOnlyList<string> SeasonConfigRotation,
    IReadOnlyList<GameCatalogRaidBossView> Bosses,
    IReadOnlyList<GameCatalogRaidBossView> Primes,
    IReadOnlyDictionary<string, GameCatalogRaidBossSeasonView> Seasons);

/// <summary>
/// One raid boss or raid-boss prime, keyed by its raw unit-set id (e.g.
/// <c>GuildBoss4Boss1OrksGhazghkull</c>). <see cref="Kind"/> is <c>boss</c> or <c>prime</c>, derived from
/// the unit-set key pattern. Optional collections are omitted from the payload when the source omits them.
/// </summary>
public sealed record GameCatalogRaidBossView(
    string UnitSetId,
    string Kind,
    bool IsPrimarch,
    string FactionId,
    int Movement,
    IReadOnlyList<GameCatalogRaidBossStatStepView> StatProgression,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<GameCatalogRaidBossWeaponView>? Weapons,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? ActiveAbilityIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? PassiveAbilityIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? RelicAbilityIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? TraitIds);

/// <summary>
/// One step of a unit's progression ladder. Core stats are non-nullable; crit/block and the relic ability
/// level appear only when the source carries them.
/// </summary>
public sealed record GameCatalogRaidBossStatStepView(
    int Health,
    int Damage,
    int FixedArmor,
    int Rank,
    int StarLevel,
    string BaseRarity,
    int ProgressionIndex,
    int AbilityLevel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? RelicAbilityLevel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? BlockChance,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? BlockDamage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? CritChance,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? CritDamage);

/// <summary>An attack profile: <see cref="Range"/> is present only for a ranged weapon.</summary>
public sealed record GameCatalogRaidBossWeaponView(
    int Hits,
    string DamageProfile,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? Range);

public sealed record GameCatalogRaidBossSeasonView(
    string SeasonConfigId,
    IReadOnlyList<GameCatalogRaidBossTierView> Tiers);

public sealed record GameCatalogRaidBossTierView(int Tier, IReadOnlyList<GameCatalogRaidBossSetView> Sets);

public sealed record GameCatalogRaidBossSetView(
    int Set,
    string ChestId,
    int GuildXp,
    IReadOnlyList<GameCatalogRaidBossEncounterView> Encounters);

/// <summary>
/// One encounter. <see cref="UnitSetId"/> is the raw <c>unitId</c> with its <c>:N</c> progression suffix
/// stripped; <see cref="ProgressionIndex"/> is that 1-based suffix (1 when absent). <see cref="FieldNpcIds"/>
/// is the ordered, de-duplicated union of the source <c>npc1id</c>/<c>npc2id</c>/<c>enemies</c> ids, each
/// likewise stripped of its suffix.
/// </summary>
public sealed record GameCatalogRaidBossEncounterView(
    int EncounterIndex,
    string EncounterType,
    string BoardId,
    int MaxNrOfTurns,
    string UnitSetId,
    int ProgressionIndex,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BossType,
    IReadOnlyList<string> FieldNpcIds,
    IReadOnlyList<string> DisallowedFactionIds,
    IReadOnlyList<GameCatalogRaidBossEncounterModifierView> Modifiers);

/// <summary>
/// An encounter modifier with its definition inlined. <see cref="HpLost"/> is the boss-HP-lost threshold
/// at which it activates; <see cref="Subtarget"/> is present only for some modifier types.
/// </summary>
public sealed record GameCatalogRaidBossEncounterModifierView(
    double HpLost,
    string ModifierId,
    string Type,
    string Target,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Subtarget,
    double Amount);
