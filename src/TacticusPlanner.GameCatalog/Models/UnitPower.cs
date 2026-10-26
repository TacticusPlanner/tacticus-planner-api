namespace TacticusPlanner.GameCatalog.Models;

/// <summary>
/// The power tables the in-game power formula reads in addition to the unit, equipment and upgrade datasets,
/// each bound from its datamine file under <c>gameconfig/units/</c> (<c>heroProgressionSteps</c>,
/// <c>heroProgressionStepsMoW</c>, <c>damageProfileModifiers</c>, <c>traitPowerModifiers</c>,
/// <c>abilityPowerModifiers</c>, <c>abilityPowerCurve</c>, and the power fields of <c>lineup</c>).
/// <see cref="Lineup"/> may list units the catalog does not have yet (unreleased); denormalization skips them.
/// </summary>
public sealed record GameCatalogRawUnitPower(
    IReadOnlyList<GameCatalogRawHeroPowerStep> HeroProgressionSteps,
    IReadOnlyList<GameCatalogRawMowPowerStep> HeroProgressionStepsMoW,
    IReadOnlyDictionary<string, int> DamageProfileModifiers,
    IReadOnlyDictionary<string, int> TraitPowerModifiers,
    IReadOnlyDictionary<string, GameCatalogRawAbilityPowerModifier> AbilityPowerModifiers,
    GameCatalogAbilityPowerCurves AbilityPowerCurve,
    IReadOnlyDictionary<string, GameCatalogRawUnitPowerEntry> Lineup
);

/// <summary>
/// One hero progression step (rarity + stars). Only the power fields are bound; the step is its index.
/// </summary>
public sealed record GameCatalogRawHeroPowerStep(
    int UnitStatMultiplierPct,
    int AbilityPowerMultiplier
);

/// <summary>One MoW progression step. <see cref="MythicAbilityPower"/> is absent before the mythic ability unlocks.</summary>
public sealed record GameCatalogRawMowPowerStep(
    int AbilityPowerMultiplier,
    int? MythicAbilityPower
);

public sealed record GameCatalogRawAbilityPowerModifier(int BaseMultiplier);

/// <summary>
/// A unit's own power fields. <see cref="PowerMultiplier"/> is a percentage, absent meaning 100.
/// <see cref="UpgradesStatIncrease"/> is one row per rank, each value the stat increase of the upgrade id
/// at the same position in <see cref="Upgrades"/> (the game's slot order); validation requires
/// <see cref="Upgrades"/> to equal the character's catalog <c>rankUpUpgrades</c> ids, so a reordered or
/// changed rank-up cannot silently pair an increase with the wrong upgrade. Both are absent for MoWs.
/// </summary>
public sealed record GameCatalogRawUnitPowerEntry(
    int? PowerMultiplier,
    IReadOnlyList<IReadOnlyList<string>>? Upgrades,
    IReadOnlyList<IReadOnlyList<int>>? UpgradesStatIncrease
);

/// <summary>
/// The ability power curves: <see cref="Active"/>/<see cref="Passive"/> indexed by ability level − 1
/// (MoW primary abilities use the active curve, secondary the passive), <see cref="Relic"/> by relic
/// level − 1.
/// </summary>
public sealed record GameCatalogAbilityPowerCurves(
    IReadOnlyList<int> Active,
    IReadOnlyList<int> Passive,
    IReadOnlyList<int> Relic
);

/// <summary>
/// The single served <c>unit-power</c> record: the power tables, progression multipliers and per-unit
/// power fields the client's in-game power formula reads. Ids and integers only; every modifier is a
/// percentage. The client derives stats and power; the server only reshapes and sorts.
/// </summary>
public sealed record GameCatalogUnitPower(
    string Id,
    IReadOnlyList<GameCatalogHeroPowerStep> HeroProgressionSteps,
    IReadOnlyList<GameCatalogMowPowerStep> MowProgressionSteps,
    IReadOnlyList<GameCatalogDamageProfileModifier> DamageProfileModifiers,
    IReadOnlyList<GameCatalogTraitPowerModifier> TraitPowerModifiers,
    IReadOnlyList<GameCatalogAbilityPowerModifier> AbilityPowerModifiers,
    GameCatalogAbilityPowerCurves AbilityPowerCurves,
    IReadOnlyList<GameCatalogUnitPowerUnit> Units
);

/// <summary>A hero progression step; <see cref="Step"/> is the progression index (0..19).</summary>
public sealed record GameCatalogHeroPowerStep(
    int Step,
    int UnitStatMultiplierPct,
    int AbilityPowerMultiplier
);

/// <summary>A MoW progression step; <see cref="MythicAbilityPower"/> is 0 before the mythic ability unlocks.</summary>
public sealed record GameCatalogMowPowerStep(
    int Step,
    int AbilityPowerMultiplier,
    int MythicAbilityPower
);

public sealed record GameCatalogDamageProfileModifier(
    string DamageType,
    int Modifier
);

/// <summary>A trait's power modifier; a catalog trait not listed has modifier 100.</summary>
public sealed record GameCatalogTraitPowerModifier(
    string TraitId,
    int Modifier
);

/// <summary>An ability's power modifier; a catalog ability not listed has modifier 100.</summary>
public sealed record GameCatalogAbilityPowerModifier(
    string AbilityId,
    int Modifier
);

/// <summary>
/// A catalog character's or MoW's power fields. <see cref="RankUpStatIncreases"/> holds one row per
/// catalog <c>rankUpUpgrades</c> row (same order as its upgrade ids); it is empty for a MoW.
/// </summary>
public sealed record GameCatalogUnitPowerUnit(
    string UnitId,
    int PowerMultiplier,
    IReadOnlyList<IReadOnlyList<int>> RankUpStatIncreases
);
