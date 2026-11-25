using System.Text.Json;
using System.Text.Json.Serialization;

namespace TacticusPlanner.GameCatalog.Models;

// ---- raw datamine shapes (internal to denormalization) -------------------------------------------

/// <summary>
/// One entry of the datamine <c>liveEvents/seasonalEvent.json</c>. Only the fields the survival-events dataset reads
/// are bound; an entry without <see cref="IncludesSurvival"/> is not a Survival event.
/// </summary>
public sealed record DatamineSeasonalEvent(
    string EventName,
    bool? IncludesSurvival,
    string? RewardProgressId,
    string? ResourceId,
    IReadOnlyList<string>? InitialOfferIds,
    int? MaxPoints,
    DatamineSeasonalEventMaxPayouts? MaxPayouts,
    DatamineSurvivalConfig? Survival);

public sealed record DatamineSeasonalEventMaxPayouts(IReadOnlyDictionary<string, int>? Points);

/// <summary>The <c>survival</c> block of a seasonal event. Medal fields are read by name from <see cref="Extra"/>.</summary>
public sealed record DatamineSurvivalConfig(
    string? FeaturedHeroId,
    int? MaxStamina,
    int? StartStamina,
    int? StaminaRegenerationTime,
    int? StaminaRegenerationAmount,
    string? BattleSetId,
    int? ScorePerTurnSkipped,
    IReadOnlyDictionary<string, int>? ScorePerKill,
    IReadOnlyDictionary<string, string>? RarityCapMilestones,
    int? StatBoostPct,
    IReadOnlyList<string>? StatBoostAffectedStats,
    IReadOnlyDictionary<string, int>? PersistentRarityUps,
    bool? IsMachinesOfWarAllowedInBattle,
    bool? UseLastStand,
    DatamineSurvivalLastStand? LastStandConfig,
    IReadOnlyList<DatamineSurvivalModifier>? Modifiers,
    IReadOnlyDictionary<string, DatamineSurvivalStatOverride>? FeaturedHeroStatOverrides)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

public sealed record DatamineSurvivalLastStand(int? StrategicMultiplier, string? StrategicBuff);

public sealed record DatamineSurvivalModifier(string? Type, IReadOnlyList<DatamineSurvivalModifierAbility>? Abilities);

public sealed record DatamineSurvivalModifierAbility(string? AbilityId, int? AbilityLevel, string? RarityCap);

public sealed record DatamineSurvivalStatOverride(int CritChance, int CritDmg, int BlockChance, int BlockDmg);

/// <summary>One Survival battle of a <c>survival_*</c> battle set (Survival sets hold exactly one).</summary>
public sealed record DatamineSurvivalBattle(
    long Power,
    IReadOnlyList<string>? DisallowedFactions,
    IReadOnlyList<DatamineSurvivalWave>? Waves);

public sealed record DatamineSurvivalWave(
    int Round,
    long Power,
    IReadOnlyList<string>? Army,
    IReadOnlyList<string>? ArmyAfterCompletion,
    int ArmyScore,
    string? RarityUpgradeOnCompletion,
    bool? IsCloneWave,
    int? CloneWaveHealthPct);

/// <summary>The datamine sources the survival-events dataset is read from.</summary>
public sealed record DatamineSurvivalSources(
    IReadOnlyList<DatamineSeasonalEvent> SeasonalEvents,
    IReadOnlyDictionary<string, JsonElement> BattleSets,
    IReadOnlyDictionary<string, List<DatamineTieredProgressReward>> TieredProgressRewards,
    IReadOnlyDictionary<string, JsonElement> Products,
    IReadOnlyDictionary<string, JsonElement> Chests,
    IReadOnlyDictionary<string, JsonElement> QuestGroups,
    IReadOnlyList<JsonElement> Offers,
    IReadOnlyDictionary<string, JsonElement> RealMoneyProducts,
    IReadOnlyList<string> FactionIds,
    IReadOnlyList<GameCatalogEventDefinition> EventDefinitions,
    IReadOnlyList<GameCatalogEventOccurrence> EventOccurrences);

/// <summary>The served survival-events id of a datamine seasonal event name.</summary>
public static class GameCatalogSurvivalEventIds
{
    private const string Prefix = "season_";
    private const string Suffix = "_event";

    /// <summary><c>season_december_2026_event</c> → <c>december-2026</c>; null when the name is not
    /// <c>season_&lt;id&gt;_event</c>.</summary>
    public static string? FromEventName(string eventName) =>
        eventName.Length > Prefix.Length + Suffix.Length
        && eventName.StartsWith(Prefix, StringComparison.Ordinal)
        && eventName.EndsWith(Suffix, StringComparison.Ordinal)
            ? eventName[Prefix.Length..^Suffix.Length].Replace('_', '-')
            : null;
}

// ---- served view (public catalog surface) --------------------------------------------------------

/// <summary>
/// One Survival event (<c>survival-events</c> dataset): a seasonal event of the datamine <c>seasonalEvent.json</c>
/// with <c>includesSurvival</c>. Ids and numbers only; the client names the event, hero, NPCs and rewards. Dates come
/// from the calendar's <c>survival</c> occurrence that names this <see cref="Id"/> and are null while the calendar does
/// not date it. See specs/survival-events-dataset.
/// </summary>
public sealed record GameCatalogSurvivalEvent(
    string Id,
    string EventKey,
    string? DefinitionId,
    DateTimeOffset? StartUtc,
    DateTimeOffset? EndUtc,
    string FeaturedCharacterId,
    string Currency,
    GameCatalogSurvivalRules Rules,
    IReadOnlyList<GameCatalogSurvivalWave> Waves,
    GameCatalogSurvivalScoring Scoring,
    GameCatalogSurvivalPoints Points,
    IReadOnlyList<GameCatalogRewardTierView> Milestones,
    IReadOnlyList<GameCatalogSurvivalChest> Chests,
    GameCatalogEventShopMissionsView Missions,
    IReadOnlyList<GameCatalogSurvivalOffer> Offers);

/// <summary>
/// Who may fight and how: allowed factions (every faction the battle does not disallow), the character and MoW caps
/// (the featured hero always fights and is not counted), stamina, the per-Stat-Boost bonus, Last Stand, the global
/// summon ability, enemy rarity-ups that stay for the rest of the run, and the featured hero's crit and block per
/// rarity.
/// </summary>
public sealed record GameCatalogSurvivalRules(
    IReadOnlyList<string> AllowedFactionIds,
    bool MowsAllowed,
    int MaxCharacters,
    int MaxMows,
    GameCatalogSurvivalStamina Stamina,
    int StatBoostPct,
    IReadOnlyList<string> StatBoostStats,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    GameCatalogSurvivalLastStand? LastStand,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    GameCatalogSurvivalSummon? Summon,
    IReadOnlyList<GameCatalogSurvivalRarityWave> PersistentRarityUps,
    IReadOnlyList<GameCatalogSurvivalStatOverride> FeaturedHeroStats);

/// <summary>Stamina at the start, the cap, and <see cref="RegenAmount"/> back every <see cref="RegenSeconds"/>.</summary>
public sealed record GameCatalogSurvivalStamina(int Start, int Max, int RegenSeconds, int RegenAmount);

/// <summary>The Last Stand buff (<c>EnvStrategicBuff:&lt;BuffId&gt;</c>) and its strategic multiplier.</summary>
public sealed record GameCatalogSurvivalLastStand(string BuffId, int StrategicMultiplier);

/// <summary>A summon the player can cast in every battle, with the ability level per rarity cap.</summary>
public sealed record GameCatalogSurvivalSummon(string UnitId, IReadOnlyList<GameCatalogSurvivalSummonLevel> Levels);

public sealed record GameCatalogSurvivalSummonLevel(string RarityCap, int AbilityLevel);

/// <summary>A rarity paired with the wave it is reached at.</summary>
public sealed record GameCatalogSurvivalRarityWave(string Rarity, int Wave);

public sealed record GameCatalogSurvivalStatOverride(
    string Rarity, int CritChance, int CritDamage, int BlockChance, int BlockDamage);

/// <summary>
/// One wave of the run, in order (<see cref="Wave"/> is 1-based; <see cref="Round"/> is the game's round number). A
/// clone wave has no enemies and fights a copy of the player's team at <see cref="CloneHealthPct"/>.
/// </summary>
public sealed record GameCatalogSurvivalWave(
    int Wave,
    int Round,
    long Power,
    int Score,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? CloneHealthPct,
    IReadOnlyList<GameCatalogSurvivalUnitCount> Enemies,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? EnemyRarityOnClear,
    IReadOnlyList<GameCatalogSurvivalPowerUp> PowerUpsOnClear);

/// <summary>An NPC at a 1-based stats row (<c>npcId:N</c>) and how many of it.</summary>
public sealed record GameCatalogSurvivalUnitCount(string NpcId, int StatsIndex, int Count);

/// <summary>
/// A power-up granted after a wave: a generic one (<c>powupHealing</c>, ...) or a summon whose <see cref="NpcId"/> and
/// <see cref="StatsIndex"/> name the NPC that spawns.
/// </summary>
public sealed record GameCatalogSurvivalPowerUp(
    string Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? NpcId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? StatsIndex,
    int Count);

/// <summary>Medals by wave cleared, points per kill, per clone wave and per skipped turn, and rarity milestones.</summary>
public sealed record GameCatalogSurvivalScoring(
    IReadOnlyList<GameCatalogSurvivalMedal> Medals,
    IReadOnlyList<GameCatalogSurvivalKillScore> KillScores,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? CloneWaveScore,
    int SkippedTurnScore,
    IReadOnlyList<GameCatalogSurvivalRarityMilestone> RarityMilestones);

public sealed record GameCatalogSurvivalMedal(string Medal, int Wave, IReadOnlyList<GameCatalogRewardRangeView> Rewards);

public sealed record GameCatalogSurvivalKillScore(string NpcId, int StatsIndex, int Score);

/// <summary>Rewards for first reaching a rarity with the featured hero.</summary>
public sealed record GameCatalogSurvivalRarityMilestone(string Rarity, IReadOnlyList<GameCatalogRewardRangeView> Rewards);

/// <summary>The most event points the event pays (<see cref="Max"/>) and the most from each source.</summary>
public sealed record GameCatalogSurvivalPoints(
    int Max,
    int FreeMissions,
    int PremiumMissions,
    int SurvivalFree,
    int SurvivalPremium);

/// <summary>A progress chest, opened in <see cref="Level"/> order, its cost and its rewards.</summary>
public sealed record GameCatalogSurvivalChest(int Level, GameCatalogSurvivalCost Cost, IReadOnlyList<GameCatalogRewardRangeView> Rewards);

public sealed record GameCatalogSurvivalCost(string Type, int Amount);

/// <summary>
/// A real-money offer of the event. <see cref="Kind"/> is the offer id's last segment (<c>pass</c>, <c>playmore</c>);
/// <see cref="PriceCents"/> is the US store price.
/// </summary>
public sealed record GameCatalogSurvivalOffer(
    string Id,
    string Kind,
    int PriceCents,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? MaxPurchases,
    IReadOnlyList<GameCatalogRewardRangeView> Rewards);
