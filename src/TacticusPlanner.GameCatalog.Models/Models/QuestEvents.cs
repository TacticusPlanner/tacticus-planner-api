using System.Text.Json;

namespace TacticusPlanner.GameCatalog.Models;

// ---- raw shape (read from the datamine linearHeroEvent.json) ---------------------------------------

/// <summary>
/// One Quest Event, read from the datamine <c>linearHeroEvent.json</c> by
/// <c>Denormalization/EventDatamineReader.cs</c>. Denormalized by <c>Denormalization/QuestEventsDenormalizer.cs</c>.
/// </summary>
public sealed record GameCatalogRawQuestEvent(
    string UnitId,
    IReadOnlyList<string> AllowedFactions,
    IReadOnlyList<string> EnemyFactions,
    IReadOnlyList<GameCatalogRawQuestTier> Tiers,
    GameCatalogQuestTokens Tokens,
    GameCatalogQuestScorePoints ScorePoints
);

public sealed record GameCatalogRawQuestTier(
    int Index,
    IReadOnlyList<GameCatalogRawQuestBattle> Battles
);

public sealed record GameCatalogRawQuestBattle(
    string BattleNr,
    string BoardId,
    int SpawnPointsSet,
    int LightningVictory,
    GameCatalogRawQuestLoot Loot,
    IReadOnlyList<GameCatalogRawQuestEnemy> Enemies,
    IReadOnlyList<GameCatalogRawQuestObjective> Objectives
);

public sealed record GameCatalogRawQuestLoot(GameCatalogRawQuestLootReward Reward);

public sealed record GameCatalogRawQuestLootReward(string UpgradeId, int Num, int Denom);

// A null progressionIndex is a board power-up, read as 1.
public sealed record GameCatalogRawQuestEnemy(string Name, int? ProgressionIndex);

// ObjectiveTarget is a string (trait, faction, damage type) or a number (hit counts) in the raw file.
public sealed record GameCatalogRawQuestObjective(
    string ObjectiveType,
    JsonElement? ObjectiveTarget,
    int Score
);

// ---- served shape ----------------------------------------------------------------------------------

public sealed record GameCatalogQuestEventView(
    string FeaturedCharacterId,
    IReadOnlyList<string> AllowedFactionIds,
    IReadOnlyList<string> EnemyFactionIds,
    IReadOnlyList<GameCatalogQuestBattleView> Battles,
    GameCatalogQuestTokens Tokens,
    GameCatalogQuestScorePoints ScorePoints
);

// The quest's token bank (the game's stamina): at most Max, regaining RegenAmount every RegenSeconds while below
// Max; RefillGemsCost is the gem price of a refill.
public sealed record GameCatalogQuestTokens(
    int Max,
    int RegenSeconds,
    int RegenAmount,
    int RefillGemsCost
);

// How a battle scores quest points, as the game ships it: PointsPerStar for each star (medal) won and at most
// MaxPointsForLightningVictory for a lightning victory.
public sealed record GameCatalogQuestScorePoints(
    int PointsPerStar,
    int MaxPointsForLightningVictory
);

public sealed record GameCatalogQuestBattleView(
    int Number,
    string Tier,
    int TierPosition,
    int DeploySlots,
    string BoardId,
    int SpawnPointsSet,
    int LightningVictoryTurns,
    IReadOnlyList<GameCatalogQuestEnemyView> Enemies,
    IReadOnlyList<GameCatalogQuestObjectiveView> Objectives,
    GameCatalogQuestLootView Loot
);

public sealed record GameCatalogQuestEnemyView(string Id, int LadderIndex, int Count);

public sealed record GameCatalogQuestObjectiveView(string Type, string? Target, int Score);

public sealed record GameCatalogQuestLootView(string UpgradeId, int ChanceNumerator, int ChanceDenominator);

// ---- quest-crates (raw and served share one shape) -------------------------------------------------

/// <summary>
/// One crate of the shared 17-crate Quest point track (wiki Quest page, Crates table). <see cref="Points"/> is
/// the running total needed to open it.
/// </summary>
public sealed record GameCatalogQuestCrate(
    int Number,
    int Points,
    IReadOnlyList<GameCatalogQuestCrateReward> Rewards
);

/// <summary>
/// One crate reward. <see cref="Type"/> is <c>Coins</c>, <c>RaidTicket</c>, <c>XpBook</c>, <c>AbilityBadge</c> or
/// <c>AscensionOrb</c>; <see cref="Min"/>..<see cref="Max"/> is the amount range (equal when fixed);
/// <see cref="Rarity"/> is set for books, badges and orbs, and <see cref="MaxRarity"/> for a badge of a random
/// rarity in <see cref="Rarity"/>..<see cref="MaxRarity"/>.
/// </summary>
public sealed record GameCatalogQuestCrateReward(
    string Type,
    int Min,
    int Max,
    string? Rarity,
    string? MaxRarity
);
