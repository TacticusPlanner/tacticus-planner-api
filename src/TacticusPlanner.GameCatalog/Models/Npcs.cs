namespace TacticusPlanner.GameCatalog.Models;

/// <summary>
/// One faction's NPCs as read from the datamine <c>npc.json</c> (source key <c>npcs-{faction}</c>; see
/// Denormalization/UnitDatamineReader.cs). <c>npcs-objects</c> is the faction-less bucket (<c>FactionId</c> =
/// <c>Objects</c>). <c>Name</c> holds the faction id: display names are not catalog data.
/// </summary>
public sealed record GameCatalogFactionNpcs(
    string Alliance,
    string FactionId,
    string Name,
    IReadOnlyList<GameCatalogRawNpc> Npcs
);

/// <summary>
/// One NPC variation as read from the datamine. Presentational fields (visual ids) are not read.
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
