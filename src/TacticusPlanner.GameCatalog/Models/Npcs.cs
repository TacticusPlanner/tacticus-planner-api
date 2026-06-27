namespace TacticusPlanner.GameCatalog.Models;

public sealed record GameCatalogFactionNpcs(
    string Alliance,
    string FactionId,
    string Name,
    IReadOnlyList<GameCatalogNpc> Npcs
);

public sealed record GameCatalogNpc(
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

public sealed record GameCatalogNpcStat(
    int AbilityLevel,
    int Damage,
    int Armour,
    int Health,
    int ProgressionIndex,
    int Rank,
    int Stars
);
