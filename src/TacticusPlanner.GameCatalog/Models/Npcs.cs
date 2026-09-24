namespace TacticusPlanner.GameCatalog.Models;

/// <summary>
/// One raw <c>npcs-{faction}.json</c> source file: the faction it describes plus its NPC variations.
/// <c>npcs-objects.json</c> is the faction-less bucket (<c>FactionId</c> = <c>Objects</c>).
/// </summary>
public sealed record GameCatalogFactionNpcs(
    string Alliance,
    string FactionId,
    string Name,
    IReadOnlyList<GameCatalogRawNpc> Npcs
);

/// <summary>
/// One NPC variation as authored in the raw per-faction file. Raw records may also carry an
/// <c>icon</c> path; it is deliberately not bound (id-only display contract) and is dropped on deserialize.
/// </summary>
public sealed record GameCatalogRawNpc(
    string Id,
    string Name,
    string MeleeDamage,
    int MeleeHits,
    string? RangedDamage,
    int? RangedHits,
    int? Distance,
    int Movement,
    IReadOnlyList<string> Traits,
    IReadOnlyList<string> ActiveAbilityDamage,
    IReadOnlyList<string> ActiveAbilities,
    IReadOnlyList<string> PassiveAbilityDamage,
    IReadOnlyList<string> PassiveAbilities,
    IReadOnlyList<GameCatalogNpcStat> Stats
);

/// <summary>
/// One served NPC variation (the flat <c>npcs</c> dataset). <see cref="FactionId"/> and
/// <see cref="Alliance"/> are stamped from the raw file the record was loaded from. <see cref="Kind"/> is
/// <c>object</c> for a record from <c>npcs-objects.json</c>, <c>machineOfWar</c> for a record carrying the
/// <c>MachineOfWar</c> trait, and <c>unit</c> otherwise — never derived from the id's spelling.
/// <see cref="Stats"/> is served in raw source order: it is neither sorted nor unique on (rank, stars).
/// </summary>
public sealed record GameCatalogNpc(
    string Id,
    string Name,
    string FactionId,
    string Alliance,
    string Kind,
    string MeleeDamage,
    int MeleeHits,
    string? RangedDamage,
    int? RangedHits,
    int? Distance,
    int Movement,
    IReadOnlyList<string> Traits,
    IReadOnlyList<string> ActiveAbilityDamage,
    IReadOnlyList<string> ActiveAbilities,
    IReadOnlyList<string> PassiveAbilityDamage,
    IReadOnlyList<string> PassiveAbilities,
    IReadOnlyList<GameCatalogNpcStat> Stats
);

public sealed record GameCatalogNpcStat(
    int AbilityLevel,
    int Damage,
    int Armour,
    int Health,
    int ProgressionIndex,
    int Rank,
    int Stars
);
