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
    // The announced run starts in run order (index = run - 1, at most 3), RFC 1123 or ISO 8601 UTC. Input to
    // the served EventStageStartDatesUtc array; not served directly. Absent in the raw file reads as empty.
    IReadOnlyList<string> RunStartDatesUtc,
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
)
{
    // System.Text.Json passes null for a missing constructor parameter; normalise it to an empty schedule.
    public IReadOnlyList<string> RunStartDatesUtc { get; init; } = RunStartDatesUtc ?? [];
}

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

// A wave enemy: the npc id, its 1-based position in that npc's served stats ladder (V1's N in "{npcId}:{N}";
// not a star count, and not checked against the ladder length) and how many of it the wave spawns.
public sealed record GameCatalogLreEnemy(
    string Id,
    int LadderIndex,
    int Count
);

public sealed record GameCatalogLreTrackEnemies(
    string Label,
    string Link
);

public sealed record GameCatalogLreRestriction(
    string Name,
    int Points,
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
    // Ids of this track's battles in the lre-battles dataset (ordered); the battle bodies live there.
    IReadOnlyList<string> BattleIds,
    IReadOnlyList<string> AvailableUnitIds
);

public sealed record GameCatalogLreView(
    // The event's unit snowprint id (e.g. "emperLucius") — used as the stable string id of the LRE.
    string Id,
    string Name,
    // Run start instants in ISO 8601 UTC, indexed by run (element 0 starts run 1); only announced runs are
    // listed, with no gaps. The client derives the current, ended and next run, and whether the event has
    // finished (all three runs over), from this array.
    IReadOnlyList<string> EventStageStartDatesUtc,
    int BattlesCount,
    int ConstraintsCount,
    // The raw mission lines parsed into typed records, in raw order (see GameCatalogLreMission).
    IReadOnlyList<GameCatalogLreMission> RegularMissions,
    IReadOnlyList<GameCatalogLreMission> PremiumMissions,
    GameCatalogLreTrackView Alpha,
    GameCatalogLreTrackView Beta,
    GameCatalogLreTrackView Gamma,
    // The event's own reward ladder, copied from its raw file (ladders differ between events).
    GameCatalogLreRewards Rewards
);

// One served event mission, parsed from its raw English line by LreDenormalizer's ordered templates. Index is its
// 0-based position in the raw list; Text is the raw line. Kind is one of the LreMissionKinds values and names
// which parameters apply; the ones it does not use are null. Faction, Alliance, DamageType and Trait carry the
// catalog's ids (units/npcs factionId, alliance, damage-type and trait strings), never the display labels.
// "Defeat N <trait> enemies" (e.g. "Daemon enemies") is kind defeatFaction with Trait set and Faction null.
public sealed record GameCatalogLreMission(
    int Index,
    string Kind,
    int Count,
    string? Faction,
    string? Alliance,
    string? DamageType,
    string? Trait,
    string Text
);

// An event's reward ladder: points milestones with their currency payouts, chest costs, the event unit's
// incremental progression shard thresholds and the shards each chest grants. The client derives every total.
public sealed record GameCatalogLreRewards(
    IReadOnlyList<GameCatalogLrePointsMilestone> PointsMilestones,
    IReadOnlyList<GameCatalogLreChestsMilestone> ChestsMilestones,
    GameCatalogLreProgression Progression,
    int ShardsPerChest
);

// A single LRE battle in the lre-battles dataset. Keyed by "{lreId}-{track}-{number}" and tagged with its
// owning event/track so the bulky wave data is fetched independently of the lightweight lres list.
public sealed record GameCatalogLreBattleView(
    string Id,
    string LreId,
    string Track,
    string MapId,
    int Number,
    int Power,
    int Tier,
    IReadOnlyList<string> DisallowedFactions,
    IReadOnlyList<GameCatalogLreWave> Waves
);

/// <summary>The served <c>kind</c> values of <see cref="GameCatalogLreMission"/>.</summary>
public static class LreMissionKinds
{
    /// <summary>Defeat N waves in any wave-based mode (and deal plain damage); <c>count</c> = N.</summary>
    public const string AnyBattle = "anyBattle";

    /// <summary>Defeat N enemies of a faction (<c>faction</c>), or of a trait-defined group such as Daemons
    /// (<c>trait</c>, <c>faction</c> null).</summary>
    public const string DefeatFaction = "defeatFaction";

    /// <summary>Use abilities N times with units of an alliance (<c>alliance</c>).</summary>
    public const string UseAbilitiesWithAlliance = "useAbilitiesWithAlliance";

    /// <summary>Defeat N waves and deal damage of a type (<c>damageType</c>).</summary>
    public const string DamageType = "damageType";

    /// <summary>Defeat N enemies with units of an alliance (<c>alliance</c>).</summary>
    public const string DefeatWithAlliance = "defeatWithAlliance";

    /// <summary>Win N battles without deploying characters with a trait (<c>trait</c>).</summary>
    public const string WinWithoutTrait = "winWithoutTrait";

    /// <summary>Win N battles without summoning any units.</summary>
    public const string WinWithoutSummon = "winWithoutSummon";

    /// <summary>Defeat N enemies with damage of a type (<c>damageType</c>).</summary>
    public const string DefeatWithDamageType = "defeatWithDamageType";
}
