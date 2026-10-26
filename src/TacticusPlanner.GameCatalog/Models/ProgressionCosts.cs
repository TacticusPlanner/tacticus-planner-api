namespace TacticusPlanner.GameCatalog.Models;

// The cost to reach a given step of the shared 20-step (rarity, stars) ascension ladder from the
// previous step (Common:None, the first step, costs nothing). Read from the datamine's
// `heroProgressionSteps.json`; see Denormalization/ProgressionCostDenormalizer.cs.
public sealed record GameCatalogAscensionCost(
    string Progression,
    int Shards,
    int MythicShards,
    int Orbs,
    string? OrbRarity
);

public sealed record GameCatalogAscensionCostView(
    string Progression,
    int Shards,
    int MythicShards,
    int Orbs,
    string? OrbRarity
);

// The shard cost to unlock a character of a given starting rarity. Read from the datamine's `heroConversion.json`.
public sealed record GameCatalogUnlockShardCost(
    string Rarity,
    int Shards
);

public sealed record GameCatalogUnlockShardCostView(
    string Rarity,
    int Shards
);

// ---- datamine shapes -------------------------------------------------------------------------------------

// One row of Data/datamine/raw/gameconfig/units/heroProgressionSteps.json. Only the fields the catalog reads
// are bound; `costs` is a key -> count map (`shards`, `mythicShards`, `heroAscensionOrb{Rarity}`).
public sealed record DatamineHeroProgressionStep(
    int Stars,
    string Rarity,
    IReadOnlyDictionary<string, int> Costs
);

// One value of Data/datamine/raw/gameconfig/units/heroConversion.json, keyed by starting rarity.
public sealed record DatamineHeroConversion(int Shards);
