namespace TacticusPlanner.GameCatalog.Models;

// ---- datamine shapes for events and onslaught (bound from Data/datamine/raw/gameconfig/) ------------------------
// Only the fields the catalog reads are bound; loca keys, visuals, offers and tracking fields are dropped.

// battles/onslaught.json: the sector tiers, the waves of each tier (one reward per round) and the per-hero reward
// rows (tier x maxProgressionIndex).
public sealed record DatamineOnslaught(
    IReadOnlyList<DatamineOnslaughtTier> Tiers,
    IReadOnlyList<DatamineOnslaughtWave> TierWaves,
    IReadOnlyList<DatamineOnslaughtHeroRewards> HonorYourHeroesRewards
);

public sealed record DatamineOnslaughtTier(int Tier);

// `rewards` is keyed by alliance; every wave of a round gives the same badge.
public sealed record DatamineOnslaughtWave(int Tier, int Round, IReadOnlyDictionary<string, string>? Rewards);

// A row applies to heroes up to maxProgressionIndex; `rewards` are "shards_[heroId]:2-3"-style strings.
public sealed record DatamineOnslaughtHeroRewards(int Tier, int MaxProgressionIndex, IReadOnlyList<string> Rewards);

// liveEvents/homeScreenEvent.json: one entry per event and power tier (eventName `..._tier_high` etc.).
public sealed record DatamineHomeScreenEvent(
    string EventName,
    string RewardProgressId,
    IReadOnlyList<DatamineHseTracker>? Trackers,
    IReadOnlyList<DatamineHseModifier>? Modifiers
);

public sealed record DatamineHseRestriction(IReadOnlyList<string>? Allowed, IReadOnlyList<string>? Disallowed);

public sealed record DatamineHseTracker(
    string Type,
    int Points,
    IReadOnlyDictionary<string, int>? PointsByRarity,
    DatamineHseRestriction? GameModeRestrictions,
    DatamineHseRestriction? TraitRestrictions,
    DatamineHseRestriction? FactionRestrictions,
    string? UnitId,
    DatamineHseRestriction? PlayerTraitRestrictions,
    DatamineHseRestriction? PlayerFactionRestrictions
);

public sealed record DatamineHseModifier(
    string Type,
    string? Operator,
    bool? PercentageModifier,
    double? Modifier,
    IReadOnlyDictionary<string, double>? Modifiers,
    bool? SameFactionBonus,
    DatamineHseRestriction? GameModeRestrictions,
    DatamineHseRestriction? TraitRestrictions,
    DatamineHseRestriction? PlayerFactionRestrictions,
    string? AbilityId
);

// One step of a loot/tieredProgressRewards.json ladder; an `endless` step is the repeat after the last milestone.
public sealed record DatamineTieredProgressReward(int RequiredProgress, string ChestRewardId, bool? Endless, int? EndlessCap);

// liveEvents/linearHeroEvent.json: one Quest Event per featured hero. `trackingAdditionalData` is a JSON string
// holding the enemy faction.
public sealed record DatamineLinearHeroEvent(
    string UnitId,
    string TrackingAdditionalData,
    IReadOnlyList<string> AllowedFactions,
    IReadOnlyList<DatamineLinearHeroTier> Tiers
);

public sealed record DatamineLinearHeroTier(int Index, IReadOnlyList<DatamineLinearHeroBattle> Battles);

// `enemies` are "npcId:progressionIndex" strings (no index for a board power-up); `loot.chanceOf` is
// "upgradeId%num/denom".
public sealed record DatamineLinearHeroBattle(
    string BattleNr,
    string BoardId,
    int SpawnPointsSet,
    int LightningVictory,
    DatamineLinearHeroLoot Loot,
    IReadOnlyList<string> Enemies,
    IReadOnlyList<GameCatalogRawQuestObjective> Objectives
);

public sealed record DatamineLinearHeroLoot(string? ChanceOf);
