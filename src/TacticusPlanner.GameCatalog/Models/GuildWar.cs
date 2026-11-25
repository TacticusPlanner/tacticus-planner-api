namespace TacticusPlanner.GameCatalog.Models;

// The served guild-war records, one per live Guild War season of the datamine `globalconfig/guildWar.json`; see
// Denormalization/GuildWarDenormalizer.cs and the guild-war-dataset spec. Ids are the game's; names and icons are the
// client's.
public sealed record GameCatalogGuildWarSeason(
    // The season key, e.g. guildWarSeasonA.
    string Id,
    // The season the game moves to next (nextGuildWarSeasonConfigId).
    string NextSeasonId,
    // Attacks a player has per war.
    int MaxAttempts,
    // The battlefield's zones in layout order (row 0 first, left to right).
    IReadOnlyList<GameCatalogGuildWarZone> Zones,
    // The zone tiers, lowest first, each with its five slot rarity caps.
    IReadOnlyList<GameCatalogGuildWarTier> Tiers,
    // The battlefield levels, ascending.
    IReadOnlyList<GameCatalogGuildWarLevel> Levels,
    // Every buff the season defines, ordered by id.
    IReadOnlyList<GameCatalogGuildWarBuff> Buffs,
    // The boards a war can be fought on, in game order.
    IReadOnlyList<GameCatalogGuildWarMap> Maps,
    GameCatalogGuildWarScoring Scoring,
    // The per-rarity lineup power caps (guildWarConfig.maxHeroPowerCap), the same on every season, in rarity order.
    IReadOnlyList<GameCatalogGuildWarPowerCap> PowerCaps
);

// A zone of the battlefield: visualId is shared by twin zones (Bunker1, Bunker2), buffId names the buff it holds (null
// when none), score is what destroying it is worth, row and column its 0-based place in the layout.
public sealed record GameCatalogGuildWarZone(
    string Id,
    string VisualId,
    string? BuffId,
    bool CanBeMoved,
    int Score,
    int Row,
    int Column
);

// A zone tier (Trooper, Veteran, Elite, Hero) and the rarity cap of each of its five defender slots.
public sealed record GameCatalogGuildWarTier(string Id, IReadOnlyList<string> RarityCaps);

// A battlefield level: the per-attempt debuff ("Grind them down"), the score factor (null on level 0) and each zone's
// tier at this level, in the season's zone order.
public sealed record GameCatalogGuildWarLevel(
    int BattlefieldLevel,
    int AttemptDebuffPercent,
    int? ScoreFactorPercent,
    IReadOnlyList<GameCatalogGuildWarLevelZone> Zones
);

public sealed record GameCatalogGuildWarLevelZone(string ZoneId, string TierId);

// A buff (EnvArmourSupplies_Regional) with its scope and the abilities it applies: one with rarity null when the game
// keys the ability by the buff's abilityId, or one per rarity when it keys them {abilityId}_{Rarity}.
public sealed record GameCatalogGuildWarBuff(
    string Id,
    string Scope,
    IReadOnlyList<GameCatalogGuildWarBuffAbility> Abilities
);

// An ability's variables as the game ships them (name -> values); the index of a value is not documented.
public sealed record GameCatalogGuildWarBuffAbility(
    string AbilityId,
    string? Rarity,
    IReadOnlyDictionary<string, IReadOnlyList<int>> Variables
);

public sealed record GameCatalogGuildWarMap(string BoardId, int SpawnPointsSet);

// What a kill and an eliminated lineup score (scoreFromUnitKilled, scoreForLineupEliminated in game order).
public sealed record GameCatalogGuildWarScoring(
    int UnitKilled,
    IReadOnlyList<GameCatalogGuildWarLineupScore> LineupEliminated
);

public sealed record GameCatalogGuildWarLineupScore(int MaxAttempts, int MaxUnitsLost, int Score);

public sealed record GameCatalogGuildWarPowerCap(string Rarity, int MaxPower);

// ---- datamine shapes -------------------------------------------------------------------------------------

// The fields of Data/datamine/raw/globalconfig/guildWar.json the catalog reads.
public sealed record DatamineGuildWar(
    Dictionary<string, DatamineGuildWarSeason>? GuildWarSeasonConfigs,
    DatamineGuildWarConfig? GuildWarConfig
);

public sealed record DatamineGuildWarConfig(Dictionary<string, int>? MaxHeroPowerCap);

public sealed record DatamineGuildWarSeason(
    int? MaxAttempts,
    Dictionary<string, int>? ScorePerZoneType,
    int? ScoreFromUnitKilled,
    List<List<string>>? DefaultZoneTypeLayout,
    Dictionary<string, DatamineGuildWarZoneType>? ZoneTypeConfigs,
    List<DatamineGuildWarMap>? MapPool,
    Dictionary<string, DatamineGuildWarAbility>? Abilities,
    List<DatamineGuildWarLevel>? Levels,
    List<DatamineGuildWarZoneTier>? ZoneTiers,
    List<DatamineGuildWarLineupScore>? ScoreForLineupEliminated,
    Dictionary<string, DatamineGuildWarBuff>? Buffs,
    string? NextGuildWarSeasonConfigId
);

public sealed record DatamineGuildWarZoneType(string? VisualId, bool? CanBeMoved, string? BuffId);

public sealed record DatamineGuildWarMap(string? BoardId, int? SpawnPointsSet);

public sealed record DatamineGuildWarAbility(Dictionary<string, List<int>>? Variables);

public sealed record DatamineGuildWarLevel(
    int? BattlefieldLevel,
    int? ScoreFactorPercent,
    int? AttemptDebuffPercent,
    List<DatamineGuildWarLevelZone>? Zones
);

public sealed record DatamineGuildWarLevelZone(string? WarZoneType, string? ZoneTierId);

public sealed record DatamineGuildWarZoneTier(string? ZoneTierId, List<string>? RarityCaps);

public sealed record DatamineGuildWarLineupScore(int? MaxAttempts, int? MaxUnitsLost, int? Score);

public sealed record DatamineGuildWarBuff(string? AbilityId, string? Scope);
