namespace TacticusPlanner.GameCatalog.Models;

public static class GameCatalogDatasets
{
    /// <summary>Public route prefix the served datasets are mounted under (used to build manifest urls).</summary>
    public const string RoutePrefix = "/api/v1/game-catalog";

    public const string MowUpgradeCosts = "mow-upgrade-costs";
    public const string CharacterAbilityCosts = "character-ability-costs";
    public const string DropChances = "drop-chances";
    public const string AscensionCosts = "ascension-costs";
    public const string UnlockShardCosts = "unlock-shard-costs";
    public const string OnslaughtRewards = "onslaught-rewards";
    public const string EventDefinitions = "event-definitions";
    public const string EventOccurrences = "event-occurrences";
    // Per-definition HSE scoring (ladders, trackers, modifiers), read from the datamine homeScreenEvent.json and
    // merged into the served event-definitions as config.hse; never served on its own. Names its validation errors.
    public const string HomeScreenEventScoring = "home-screen-event-scoring";
    // horde-mode-tiers is a single curated S/A/B/C/D tier list of characters for horde modes, with each
    // tier's power multiplier; denormalization only sorts the characters by unit id.
    // See Models/HordeModeTiers.cs.
    public const string HordeModeTiers = "horde-mode-tiers";
    public const string MythicQuests = "mythic-quests";
    // quest-events: the datamine's Quest Events (linearHeroEvent.json; one record per featured hero, 17 battles
    // each). quest-crates: the shared 17-crate point track. Both are served under the same key.
    // See Denormalization/QuestEventsDenormalizer.cs.
    public const string QuestEvents = "quest-events";
    public const string QuestCrates = "quest-crates";
    // unit-power is the single record of power tables, progression multipliers and per-unit power fields
    // the client's in-game power formula reads (read from the datamine power tables). See Models/UnitPower.cs.
    public const string UnitPower = "unit-power";

    // Route/key prefixes for the split dataset families.
    public const string UnitsPrefix = "units";
    public const string ShopsPrefix = "shops";
    public const string NpcsPrefix = "npcs";
    public const string CampaignBattlesPrefix = "campaign-battles";
    public const string EquipmentPrefix = "equipment";
    public const string UpgradesPrefix = "upgrades";
    public const string LresPrefix = "lres";

    /// <summary>
    /// Campaign-battle datasets grouped by core characters (storyline x side). These are source-file bucket
    /// names (dataset keys) and are decoupled from each group's <c>groupId</c> content field.
    /// </summary>
    /// <remarks>
    /// Every group's <c>groupId</c> and battle-level <c>type</c> (replacing the old <c>difficulty</c> string)
    /// are aligned to the Tacticus player API's own campaign-progress ids/types (see ADR 0007 in the docs
    /// repo and <c>TacticusApi.Models.Player.CampaignProgress</c>). Storylines/mirrors are four fully
    /// distinct groups per storyline, matching Tacticus's own four distinct ids: <c>campaign1..4</c>
    /// (type <c>Standard</c>), <c>mirror1..4</c> (type <c>Mirror</c>), <c>elite1..4</c> (type <c>Elite</c>),
    /// <c>eliteMirror1..4</c> (type <c>EliteMirror</c>) — confirmed against a real player response. Elite and
    /// EliteMirror battle content was not actually missing: it was embedded as <c>difficulty:"elite"</c>
    /// battles inside the <c>campaignN</c>/<c>mirrorN</c> files respectively; splitting them into their own
    /// groups only re-keys existing data, no new content was authored. Campaign events, by contrast, keep a
    /// single group per event id (confirmed via V1's `campaign-mapper-service.ts` ordinal event-id mapping,
    /// cross-checked against each group's own faction/enemy-faction fields): <c>death-guard-vs-admech</c> -&gt;
    /// <c>eventCampaign1</c>, <c>ultramarines-vs-tyranids</c> -&gt; <c>eventCampaign2</c>,
    /// <c>genestealers-vs-tau-empire</c> -&gt; <c>eventCampaign3</c>,
    /// <c>adepta-sororitas-vs-death-guard</c> -&gt; <c>eventCampaign4</c>,
    /// <c>world-eaters-vs-adepta-sororitas</c> -&gt; <c>eventCampaign5</c>,
    /// <c>necrons-vs-dark-angels</c> -&gt; <c>eventCampaign6</c> — because Tacticus itself reports event
    /// progress as one id with multiple <c>type</c> values (<c>Standard</c>/<c>Extremis</c>), not separate
    /// ids. Each event battle carries its own <c>type</c> (<c>Standard</c>/<c>Extremis</c>) plus a
    /// <c>challenge</c> flag (the old <c>eventStandardChallenge</c>/<c>eventExtremisChallenge</c> tiers) — a
    /// finer split than Tacticus's own <c>type</c> field, kept battle-level rather than promoted to a fifth
    /// group per event.
    /// </remarks>
    public static readonly IReadOnlyList<string> CampaignBattleGroups =
    [
        "campaign-battles-indomitus",
        "campaign-battles-indomitus-elite",
        "campaign-battles-indomitus-mirror",
        "campaign-battles-indomitus-mirror-elite",
        "campaign-battles-fall-of-cadia",
        "campaign-battles-fall-of-cadia-elite",
        "campaign-battles-fall-of-cadia-mirror",
        "campaign-battles-fall-of-cadia-mirror-elite",
        "campaign-battles-octarius",
        "campaign-battles-octarius-elite",
        "campaign-battles-octarius-mirror",
        "campaign-battles-octarius-mirror-elite",
        "campaign-battles-saim-hann",
        "campaign-battles-saim-hann-elite",
        "campaign-battles-saim-hann-mirror",
        "campaign-battles-saim-hann-mirror-elite",
        "campaign-battles-death-guard-vs-admech",
        "campaign-battles-adepta-sororitas-vs-death-guard",
        "campaign-battles-ultramarines-vs-tyranids",
        "campaign-battles-genestealers-vs-tau-empire",
        "campaign-battles-world-eaters-vs-adepta-sororitas",
        "campaign-battles-necrons-vs-dark-angels",
    ];

    /// <summary>
    /// Curated campaign group metadata (faction, release type, allies, core characters) and, per campaign type, the
    /// datamine campaign and battle-id prefix of each <see cref="CampaignBattleGroups"/> key; the battles themselves
    /// are read from the datamine (see Denormalization/CampaignDatamineReader.cs).
    /// </summary>
    public const string CampaignCuration = "campaign-curation";

    /// <summary>
    /// Curated Legendary Release Event data the game has no source for (display names, run starts, battle
    /// completion points, allowed-units filters, restriction names), keyed by event unit. Its keys pick the served
    /// events; everything else is read from the datamine (see Denormalization/LreDatamineReader.cs).
    /// </summary>
    public const string LreCuration = "lre-curation";

    /// <summary>
    /// Source keys of the always-on daily shops, each read from its merchant in the datamine <c>merchants.json</c>.
    /// Consolidated into the single served <see cref="Shops"/> array (one record per shop, keyed by the id after
    /// the <c>shops-</c> prefix). Limited-time event shops are deliberately excluded.
    /// </summary>
    public static readonly IReadOnlyList<string> ShopSources =
    [
        "shops-guild",
        "shops-war",
        "shops-rogue-trader",
        "shops-crusade",
    ];

    public const string GuildRaidMeta = "guild-raid-meta";

    /// <summary>
    /// Raw Comp-guidance source (<c>guild-raid-comps.json</c>): <c>sourceId</c>/<c>updatedOn</c> plus the
    /// Comp profiles. Merged with the per-boss files into the single served <see cref="GuildRaidMeta"/>
    /// dataset; no raw file is served directly.
    /// </summary>
    public const string GuildRaidMetaComps = "guild-raid-comps";

    /// <summary>
    /// Per-boss raw Guild Raid Meta source files, one per raid boss, named after the boss's
    /// <c>GuildBoss{n}Boss1</c> unit-set key. Key = <c>guild-raid-meta-boss-{n}-{Slug}</c>. Each file's <c>bosses[]</c> entry and any
    /// <c>primes[]</c> entries are merged into the assembled raw data.
    /// </summary>
    public static readonly IReadOnlyList<string> GuildRaidMetaBossGroups =
    [
        "guild-raid-meta-boss-1-TyranTervigonLeviathan",
        "guild-raid-meta-boss-2-TyranHiveTyrantLeviathan",
        "guild-raid-meta-boss-3-NecroSilentKing",
        "guild-raid-meta-boss-4-OrksGhazghkull",
        "guild-raid-meta-boss-5-DeathMortarion",
        "guild-raid-meta-boss-6-TyranScreamerKiller",
        "guild-raid-meta-boss-7-AstraRogaldorn",
        "guild-raid-meta-boss-8-EldarAvatar",
        "guild-raid-meta-boss-9-ThousMagnus",
        "guild-raid-meta-boss-10-AdmecBelisarius",
        "guild-raid-meta-boss-11-TauRiptide",
        "guild-raid-meta-boss-12-DarkaLion",
    ];

    /// <summary>Raw embedded source datasets (one per file) used to build the served catalog.</summary>
    public static readonly IReadOnlyList<string> Required =
    [
        DropChances,
        EventDefinitions,
        EventOccurrences,
        HordeModeTiers,
        QuestCrates,
        CampaignCuration,
        LreCuration,
        GuildRaidMetaComps,
        .. GuildRaidMetaBossGroups,
    ];

    // Served (denormalized) dataset keys — the public manifest surface. Each is one consolidated,
    // self-contained dataset computed from the raw source (reference tables inlined; no client joins).
    public const string Characters = "characters";
    public const string Npcs = "npcs";
    public const string Mows = "mows";
    // The shared mow upgrade-cost ladder, served as its own dataset (it is a single progression shared by
    // every mow, so it is not inlined per record).
    public const string MowUpgradeCostsServed = MowUpgradeCosts;
    // The shared character ability cost ladder (gold + ability badges per level raised to), one progression
    // shared by every character and ability track.
    public const string CharacterAbilityCostsServed = CharacterAbilityCosts;
    // The shared ascension-orb/shard cost ladder (one entry per progression step, keyed by the same
    // "{Rarity}:{Stars}" strings the client's Progression type uses) and the per-rarity unlock-shard
    // cost table — both single shared progressions, so served as their own datasets rather than inlined
    // per character.
    public const string AscensionCostsServed = AscensionCosts;
    public const string UnlockShardCostsServed = UnlockShardCosts;
    public const string Upgrades = "upgrades";
    public const string Equipment = "equipment";
    // campaign-battles is keyed by battle id (each carries its campaignGroupId); campaign-definitions is
    // keyed by groupId and references only the battle ids belonging to the group.
    public const string CampaignBattles = "campaign-battles";
    public const string CampaignDefinitions = "campaign-definitions";
    // lres is the lightweight per-event list (tracks reference their battles by id); lre-battles holds the
    // bulky per-battle wave data (keyed "{lreId}-{track}-{number}"). Each lres record carries its own reward
    // ladder; there is no shared ladder dataset (ladders differ between events).
    public const string Lres = "lres";
    public const string LreBattles = "lre-battles";
    // event-definitions is served with each Home Screen Event definition's scoring merged into its config as
    // config.hse (from the raw home-screen-event-scoring file). events-calendar is the denormalized, date-indexed projection built from
    // event-definitions + event-occurrences (see Denormalization/EventsDenormalizer.cs); raw
    // event-occurrences itself is never served directly — see design.md Decision 1 in
    // add-game-events-calendar-dataset.
    public const string EventDefinitionsServed = EventDefinitions;
    public const string EventsCalendar = "events-calendar";
    // shops is a plain array, one record per always-on daily shop (id = guild / war / rogue-trader /
    // crusade), consolidated from the four ShopSources merchants. The raw per-shop files are never served
    // directly. Reward/free-offer "type:qty" strings and the Quartz cronSchedule are normalized to
    // structured values + an explicit day-of-week list at build time (see Denormalization/ShopsDenormalizer.cs).
    public const string Shops = "shops";
    // raid-bosses is one consolidated, self-contained dataset assembled from the datamine guildBoss files:
    // the season-config rotation, the raid bosses and raid-boss primes (identity + progression ladder +
    // weapons + ability/trait ids), and the season configs whose encounters carry their referenced unit-set
    // id + progression index, field-npc ids, and resolved modifier definitions inlined. No raw file is
    // served directly. See Denormalization/RaidBossDenormalizer.cs and specs/raid-bosses-dataset.
    public const string RaidBosses = "raid-bosses";
    // mythic-quests is a plain array, one record per character with quests (ordered by unitId), built from the
    // mythic_ quests of the datamine hero quest group. Reward "type[:amount]" strings are parsed into
    // structured records; task text templates are not served (they are client game text keyed by locaKey).
    // See Denormalization/MythicQuestDenormalizer.cs.
    public const string MythicQuestsServed = MythicQuests;

    /// <summary>The denormalized datasets exposed by the manifest / served by the catalog endpoints.</summary>
    public static readonly IReadOnlyList<string> Served =
    [
        Characters,
        Npcs,
        Mows,
        MowUpgradeCostsServed,
        CharacterAbilityCostsServed,
        AscensionCostsServed,
        UnlockShardCostsServed,
        OnslaughtRewards,
        Upgrades,
        Equipment,
        CampaignBattles,
        CampaignDefinitions,
        Lres,
        LreBattles,
        HordeModeTiers,
        EventDefinitionsServed,
        EventsCalendar,
        Shops,
        RaidBosses,
        GuildRaidMeta,
        MythicQuestsServed,
        QuestEvents,
        QuestCrates,
        UnitPower,
    ];
}
