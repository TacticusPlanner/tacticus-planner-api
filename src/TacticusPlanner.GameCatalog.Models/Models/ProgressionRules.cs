namespace TacticusPlanner.GameCatalog.Models;

// The single served progression-rules record: the XP curve, XP books, the XP level each rank-up upgrade slot needs and
// the per-step caps and multipliers of the hero and MoW ascension ladders. Read from the datamine `units/xpLevels.json`,
// `units/upgradeSlots.json`, `consumables.json`, `units/heroProgressionSteps.json` and
// `units/heroProgressionStepsMoW.json`; see Denormalization/ProgressionRulesDenormalizer.cs. Field names are the game's.
public sealed record GameCatalogProgressionRules(
    // The game's xpLevels: element i is the total XP a unit needs to reach level i + 2 (level 1 needs none).
    IReadOnlyList<int> XpLevels,
    IReadOnlyList<GameCatalogXpBook> XpBooks,
    IReadOnlyList<GameCatalogUpgradeSlotRank> UpgradeSlots,
    IReadOnlyList<GameCatalogHeroProgressionRule> HeroProgressionSteps,
    IReadOnlyList<GameCatalogMowProgressionRule> MowProgressionSteps
);

// One XP book: its id, rarity, the XP it gives and the gold it costs to apply.
public sealed record GameCatalogXpBook(
    string Id,
    string Rarity,
    int XpIncrease,
    int Gold
);

// The XP level each of a rank's six upgrade slots needs before it can be applied, in the game's slot order, keyed by the
// rank the upgrades are applied at (Stone1 is the first row).
public sealed record GameCatalogUpgradeSlotRank(
    string Rank,
    IReadOnlyList<int> XpLevelRequirements
);

// One step of the hero ascension ladder: its "{Rarity}:{Stars}" progression and the caps it allows. MaxRank is the
// game's rank index as a rank id (3 = Iron1); HreShards is the shards a Hero Release Event ascension to this step costs
// (null when the game lists none).
public sealed record GameCatalogHeroProgressionRule(
    int Step,
    string Progression,
    string Rarity,
    int Stars,
    int MaxXpLevel,
    string MaxRank,
    int UnitStatMultiplierPct,
    int AbilityStatMultiplierPct,
    int? HreShards
);

// One step of the MoW ascension ladder. MythicAbilityStatMultiplierPct is null below Mythic.
public sealed record GameCatalogMowProgressionRule(
    int Step,
    string Progression,
    string Rarity,
    int Stars,
    int MaxAbilityLevel,
    int AbilityStatMultiplierPct,
    int? MythicAbilityStatMultiplierPct
);

// ---- datamine shapes -------------------------------------------------------------------------------------

// Data/datamine/raw/gameconfig/units/upgradeSlots.json.
public sealed record DatamineUpgradeSlots(IReadOnlyList<IReadOnlyList<int>>? XpLevelRequirements);

// Data/datamine/raw/gameconfig/consumables.json (only xpBooks is bound).
public sealed record DatamineConsumables(IReadOnlyList<DatamineXpBook>? XpBooks);

public sealed record DatamineXpBook(string? Id, string? Rarity, int XpIncrease, int Gold);

// One row of heroProgressionSteps.json with the fields progression-rules reads.
public sealed record DatamineHeroProgressionRule(
    int Stars,
    string Rarity,
    int MaxXpLevel,
    int MaxRank,
    int UnitStatMultiplierPct,
    int AbilityStatMultiplierPct,
    IReadOnlyDictionary<string, int>? AscensionCosts
);

// One row of heroProgressionStepsMoW.json with the fields progression-rules reads.
public sealed record DatamineMowProgressionRule(
    int Stars,
    string Rarity,
    int MaxAbilityLevel,
    int AbilityStatMultiplierPct,
    int? MythicAbilityStatMultiplierPct
);
