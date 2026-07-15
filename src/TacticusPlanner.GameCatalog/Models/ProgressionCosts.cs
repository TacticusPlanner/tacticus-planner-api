namespace TacticusPlanner.GameCatalog.Models;

// The cost to reach a given step of the shared 20-step (rarity, stars) ascension ladder from the
// previous step (Common:None, the first step, costs nothing). Ported from V1's `charsProgression`
// (shards/mythicShards) reconciled against `OrbAscensionCalculator.UPGRADE_PATH` (orbs/orbRarity) —
// the two V1 tables disagreed only at Mythic:MythicWings (20 vs 25 orbs); this dataset uses 25, from
// the dedicated orb calculator.
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

// The shard cost to unlock a character of a given starting rarity. Ported from V1's `charsUnlockShards`.
public sealed record GameCatalogUnlockShardCost(
    string Rarity,
    int Shards
);

public sealed record GameCatalogUnlockShardCostView(
    string Rarity,
    int Shards
);
