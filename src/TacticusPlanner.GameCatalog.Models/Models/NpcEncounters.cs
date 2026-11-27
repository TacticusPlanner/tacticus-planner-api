namespace TacticusPlanner.GameCatalog.Models;

// One served npc-encounters record: every battle of one source and group (a campaign, an event, a raid boss, a
// Survival season or Onslaught) in which the npc fights as an enemy. Built from the other served views (and the raw
// Survival / Onslaught waves) by Denormalization/NpcEncounterDenormalizer.cs. Ids and numbers only: the client
// resolves names, links and timing.
public sealed record GameCatalogNpcEncounter(
    string NpcId,
    string Source,
    string GroupId,
    IReadOnlyList<GameCatalogNpcEncounterBattle> Battles
);

// One place within the group. Every field is always written: the nullable ones as null and Rounds as [] where the
// source has no such value (see NpcEncounterSources for which source sets which field). Count is how many of the npc
// the place spawns: the battle's total, a Survival round's (one entry per round), or the most in any one Onslaught
// wave of the tier, since a tier draws from a pool of waves. A guild raid names no count, so it is null there.
// Levels split the count by level (one npc can fight at several in one place), lowest first; a guild raid has none.
public sealed record GameCatalogNpcEncounterBattle(
    string? BattleId,
    string? Track,
    int? Tier,
    int? Number,
    bool Challenge,
    string Role,
    IReadOnlyList<int> Rounds,
    int? Count,
    IReadOnlyList<GameCatalogNpcEncounterLevel> Levels
);

// How many of the npc fight at one level. ServedIndex is the 0-based position in the npc's served stats (the NPC
// Library's level), or null when the source's index points past the ladder, which a few boss entries do. An
// Onslaught count is the most at that level in any one wave of the tier, so they can add up to more than the
// battle's Count.
public sealed record GameCatalogNpcEncounterLevel(int? ServedIndex, int Count);

/// <summary>The served <c>source</c> values of <see cref="GameCatalogNpcEncounter"/>, in served order.</summary>
public static class NpcEncounterSources
{
    /// <summary>A storyline campaign battle (Standard, Mirror, Elite, Elite Mirror).</summary>
    public const string Campaign = "campaign";

    /// <summary>An event campaign battle; the battle's track is Standard or Extremis.</summary>
    public const string CampaignEvent = "campaignEvent";

    /// <summary>A Legendary Event battle; the group is the event's lreId.</summary>
    public const string LegendaryEvent = "legendaryEvent";

    /// <summary>A Quest Event battle; the group is the quest's featured character.</summary>
    public const string QuestEvent = "questEvent";

    /// <summary>An Incursion battle; the group is the Machine of War.</summary>
    public const string Incursion = "incursion";

    /// <summary>A guild raid encounter; the group is the boss / prime unit set.</summary>
    public const string GuildRaid = "guildRaid";

    /// <summary>A Survival season; the group is the battle set key.</summary>
    public const string Survival = "survival";

    /// <summary>An Onslaught wave; the single group is <see cref="OnslaughtGroupId"/>.</summary>
    public const string Onslaught = "onslaught";

    /// <summary>The group id of every Onslaught record.</summary>
    public const string OnslaughtGroupId = "onslaught";

    /// <summary>Every source, in the order records are served.</summary>
    public static readonly IReadOnlyList<string> Ordered =
        [Campaign, CampaignEvent, LegendaryEvent, QuestEvent, Incursion, GuildRaid, Survival, Onslaught];
}

/// <summary>The served <c>role</c> values of <see cref="GameCatalogNpcEncounterBattle"/>.</summary>
public static class NpcEncounterRoles
{
    /// <summary>The npc is one of the enemies (every source).</summary>
    public const string Enemy = "enemy";

    /// <summary>The npc is the guild raid encounter's boss itself.</summary>
    public const string Boss = "boss";

    /// <summary>The npc is the guild raid encounter's prime itself.</summary>
    public const string Prime = "prime";
}

// ---- datamine shapes read only for npc-encounters ----------------------------------------------------------

// One wave of a battleSets.json battle (Survival reads `round` and `army`; other fields are not bound).
public sealed record DatamineBattleSetWave(int Round, IReadOnlyList<string>? Army);

// One battle of a battleSets.json set.
public sealed record DatamineBattleSetBattle(IReadOnlyList<DatamineBattleSetWave> Waves);

// The raw waves of one Survival season (a battleSets.json `survival_*` set), keyed by the set key.
public sealed record GameCatalogRawSurvivalSeason(string Id, IReadOnlyList<DatamineBattleSetWave> Waves);
