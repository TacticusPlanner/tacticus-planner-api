namespace TacticusPlanner.GameCatalog.Models;

// A point-reward milestone: the cumulative points needed to reach it and the engram payout it grants.
public sealed record GameCatalogLrePointsMilestone(
    int Milestone,
    int CumulativePoints,
    int EngramPayout
);

// A chest rung: the chest level and the engram cost to open it.
public sealed record GameCatalogLreChestsMilestone(
    int ChestLevel,
    int EngramCost
);

// The points awarded for reaching each character progression tier during the event.
public sealed record GameCatalogLreProgression(
    int Unlock,
    int FourStars,
    int FiveStars,
    int BlueStar,
    int Mythic,
    int TwoBlueStars
);

public sealed record GameCatalogLre(
    string SourceFile,
    int Id,
    string UnitSnowprintId,
    string Name,
    string WikiLink,
    int EventStage,
    bool Finished,
    string? NextEventDate,
    string? NextEventDateUtc,
    int BattlesCount,
    int ConstraintsCount,
    IReadOnlyList<string> RegularMissions,
    IReadOnlyList<string> PremiumMissions,
    GameCatalogLreTrack Alpha,
    GameCatalogLreTrack Beta,
    GameCatalogLreTrack Gamma,
    IReadOnlyList<GameCatalogLrePointsMilestone> PointsMilestones,
    IReadOnlyList<GameCatalogLreChestsMilestone> ChestsMilestones,
    int ShardsPerChest,
    GameCatalogLreProgression Progression
);

public sealed record GameCatalogLreTrack(
    string Name,
    GameCatalogLreTrackEnemies Enemies,
    int KillPoints,
    IReadOnlyList<int> BattlesPoints,
    IReadOnlyList<int> DefeatAll,
    IReadOnlyList<GameCatalogLreFilter> AllowedUnitsFilter,
    IReadOnlyList<GameCatalogLreRestriction> UnitsRestrictions,
    IReadOnlyList<GameCatalogLreBattle> Battles
);

public sealed record GameCatalogLreBattle(
    string MapId,
    int Number,
    int Power,
    int Tier,
    IReadOnlyList<string> DisallowedFactions,
    IReadOnlyList<GameCatalogLreWave> Waves
);

public sealed record GameCatalogLreWave(
    int Round,
    int Power,
    IReadOnlyList<GameCatalogLreEnemy> Enemies
);

public sealed record GameCatalogLreEnemy(
    string Id,
    int Stars,
    int Count
);

public sealed record GameCatalogLreTrackEnemies(
    string Label,
    string Link
);

public sealed record GameCatalogLreRestriction(
    string Name,
    int Points,
    string? IconId,
    int Index,
    GameCatalogLreFilter Filter
);

public sealed record GameCatalogLreFilter(
    string Kind,
    string Target,
    bool Exclude
);

public sealed record GameCatalogLreTrackView(
    string Name,
    GameCatalogLreTrackEnemies Enemies,
    int KillPoints,
    IReadOnlyList<int> BattlesPoints,
    IReadOnlyList<int> DefeatAll,
    IReadOnlyList<GameCatalogLreFilter> AllowedUnitsFilter,
    IReadOnlyList<GameCatalogLreRestriction> UnitsRestrictions,
    IReadOnlyList<GameCatalogLreBattle> Battles,
    IReadOnlyList<string> AvailableUnitIds
);

public sealed record GameCatalogLreView(
    // The event's unit snowprint id (e.g. "emperLucius") — used as the stable string id of the LRE.
    string Id,
    string Name,
    string WikiLink,
    int EventStage,
    bool Finished,
    string? NextEventDate,
    string? NextEventDateUtc,
    int BattlesCount,
    int ConstraintsCount,
    IReadOnlyList<string> RegularMissions,
    IReadOnlyList<string> PremiumMissions,
    GameCatalogLreTrackView Alpha,
    GameCatalogLreTrackView Beta,
    GameCatalogLreTrackView Gamma,
    IReadOnlyList<GameCatalogLrePointsMilestone> PointsMilestones,
    IReadOnlyList<GameCatalogLreChestsMilestone> ChestsMilestones,
    int ShardsPerChest,
    GameCatalogLreProgression Progression
);
