namespace TacticusPlanner.GameCatalog.Models;

/// <summary>
/// The raw <c>unit-power.json</c>: V1's slice of Snowprint's client game config that the in-game power
/// formula reads in addition to the unit, equipment and upgrade datasets. Keys keep V1's names
/// (<c>heroProgressionStepsMoW</c>, <c>abilityPowerCurve</c>, <c>lineup</c>) so a game patch can be
/// reconciled field by field. <see cref="Lineup"/> may list units the catalog does not have yet
/// (unreleased); denormalization skips them.
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
/// One hero progression step (rarity + stars). <c>rarity</c>/<c>stars</c> are kept in the raw file for
/// readability only; the step is its index.
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
/// <see cref="UpgradesStatIncrease"/> is one row per rank, aligned with the character's
/// <c>rankUpUpgrades</c> upgrade ids (absent for MoWs).
/// </summary>
public sealed record GameCatalogRawUnitPowerEntry(
    int? PowerMultiplier,
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
