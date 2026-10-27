namespace TacticusPlanner.GameCatalog.Models;

// One served Hero Release Event (the hre dataset), read from the datamine's `liveEvents/heroReleaseEvent.json` with its
// mission chain (`quests/groups.json`), leaderboard (`leaderboards.json`), chests (`loot/chests.json`) and chest shop
// (`shop/products.json`); see Denormalization/HreDenormalizer.cs. Field names are the game's; "currency" is the event's
// hreCurrency and "shards" its hreShards.
public sealed record GameCatalogHre(
    string Id,
    string HeroId,
    string BaseRarity,
    string MaxRarity,
    string FeaturedFaction,
    IReadOnlyList<string> GameModes,
    GameCatalogHreScorePoints ScorePoints,
    IReadOnlyList<GameCatalogHreMilestone> Milestones,
    IReadOnlyList<GameCatalogHreMission> Missions,
    GameCatalogHreLeaderboard Leaderboard,
    IReadOnlyList<GameCatalogHreChest> ChestShop,
    GameCatalogHreOhSoCloseOffer OhSoCloseOffer
);

// How battles score event points, as the game ships it.
public sealed record GameCatalogHreScorePoints(
    int CampaignScorePointsDifficultyMultiplier,
    int CampaignScorePointsLightningVictoryDifficultyMultiplier,
    int CampaignScorePointsMainUnitsDeadMultiplier,
    int CampaignScorePointsStaminaMultiplier,
    int PvpScorePointsDifficultyMultiplier,
    IReadOnlyList<long> ScorePointsEnemyPowerTable,
    IReadOnlyDictionary<string, IReadOnlyList<int>> GuildBossScorePointsDamageTableByRarity
);

// A points milestone and the event currency it pays (not cumulative).
public sealed record GameCatalogHreMilestone(int ScorePoints, int Currency);

// One mission of the event's quest chain, in chain order (Index 1 first); Bonus marks the chain's last, bonus mission.
// Currency is its hreCurrency reward (0 when it pays none); Rewards are the game's "type[:amount]" strings.
public sealed record GameCatalogHreMission(
    int Index,
    string Id,
    bool Bonus,
    int Currency,
    IReadOnlyList<string> Rewards,
    IReadOnlyList<GameCatalogHreMissionTask> Tasks
);

// One task of a mission: the game's task name, target and parameters (factionTrait, factionId, gameMode, heroId,
// damageProfileType).
public sealed record GameCatalogHreMissionTask(
    string Name,
    int Target,
    IReadOnlyDictionary<string, string> Parameters
);

// The event leaderboard: the daily currency per position bracket and each phase's end-of-phase chest per bracket.
// Position is the bracket's first 0-based position (0 is first place; a bracket runs to the next one's position).
public sealed record GameCatalogHreLeaderboard(
    string Id,
    IReadOnlyList<GameCatalogHreDailyReward> DailyRewards,
    IReadOnlyList<GameCatalogHrePhase> Phases
);

public sealed record GameCatalogHreDailyReward(int Position, int Currency);

// One reward phase; EndOffsetDays is null for the last phase (it ends with the event).
public sealed record GameCatalogHrePhase(int? EndOffsetDays, IReadOnlyList<GameCatalogHrePhaseChest> Tiers);

public sealed record GameCatalogHrePhaseChest(int Position, string ChestId, int Currency, int Shards);

// One chest of the event chest shop, in purchase order: its currency cost and shard range.
public sealed record GameCatalogHreChest(string ChestId, int Cost, int MinShards, int MaxShards);

// The game's "Oh So Close" offer pricing for the hero's unlock and ascension shards.
public sealed record GameCatalogHreOhSoCloseOffer(
    GameCatalogHreOfferPrice Unlock,
    GameCatalogHreOfferPrice Ascension
);

public sealed record GameCatalogHreOfferPrice(int Base, int Multiplier);

// ---- datamine shapes -------------------------------------------------------------------------------------

// One entry of Data/datamine/raw/gameconfig/liveEvents/heroReleaseEvent.json (display and tracking fields not bound).
public sealed record DatamineHeroReleaseEvent(
    string EventName,
    string? TrackingFeaturedFaction,
    IReadOnlyList<string>? GameModes,
    DatamineHreUnlockableHero? UnlockableHero,
    DatamineHreScorePoints? ScorePoints,
    string? QuestId,
    GameCatalogHreOhSoCloseOffer? OhSoCloseOffer
);

public sealed record DatamineHreUnlockableHero(string? HeroId, string? BaseRarity, string? MaxRarity);

public sealed record DatamineHreScorePoints(
    int CampaignScorePointsDifficultyMultiplier,
    int CampaignScorePointsLightningVictoryDifficultyMultiplier,
    int CampaignScorePointsMainUnitsDeadMultiplier,
    int CampaignScorePointsStaminaMultiplier,
    int PvpScorePointsDifficultyMultiplier,
    IReadOnlyList<long>? ScorePointsEnemyPowerTable,
    IReadOnlyDictionary<string, IReadOnlyList<int>>? GuildBossScorePointsDamageTableByRarity,
    IReadOnlyList<DatamineHreMilestone>? Rewards
);

public sealed record DatamineHreMilestone(int ScorePoints, string? Reward);

// One quest of an hre{N} group of quests/groups.json.
public sealed record DatamineHreQuest(
    string Name,
    IReadOnlyList<string>? Rewards,
    IReadOnlyList<string>? SuccessorQuests,
    IReadOnlyList<DatamineHreQuestTask>? Tasks
);

public sealed record DatamineHreQuestTask(string Name, int Target, IReadOnlyDictionary<string, string>? TaskParameters);

public sealed record DatamineHreQuestGroup(IReadOnlyList<DatamineHreQuest> Quests);

// One leaderboard of leaderboards.json.
public sealed record DatamineLeaderboard(
    IReadOnlyList<DatamineLeaderboardPositionReward>? PositionRewards,
    IReadOnlyList<DatamineLeaderboardSchedule>? RewardSchedules
);

public sealed record DatamineLeaderboardPositionReward(int Position, string RewardId);

public sealed record DatamineLeaderboardSchedule(DatamineLeaderboardOffset? EndOffset, IReadOnlyList<DatamineLeaderboardTier> RewardTiers);

public sealed record DatamineLeaderboardOffset(int Days);

public sealed record DatamineLeaderboardTier(int PositionAbsolute, string RewardId);

public sealed record DatamineLeaderboards(IReadOnlyDictionary<string, DatamineLeaderboard> Leaderboards);

public sealed record DatamineHreSources(
    IReadOnlyList<DatamineHeroReleaseEvent> Events,
    IReadOnlyDictionary<string, System.Text.Json.JsonElement> QuestGroups,
    DatamineLeaderboards Leaderboards,
    IReadOnlyDictionary<string, System.Text.Json.JsonElement> Chests,
    IReadOnlyDictionary<string, System.Text.Json.JsonElement> Products
);
