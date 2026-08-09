namespace TacticusPlanner.GameCatalog.Models;

public static class GameCatalogDatasets
{
    /// <summary>Public route prefix the served datasets are mounted under (used to build manifest urls).</summary>
    public const string RoutePrefix = "/api/v1/game-catalog";

    public const string MowUpgradeCosts = "mow-upgrade-costs";
    public const string EquipmentUpgradeCosts = "equipment-upgrade-costs";
    public const string DropChances = "drop-chances";
    public const string AscensionCosts = "ascension-costs";
    public const string UnlockShardCosts = "unlock-shard-costs";
    public const string OnslaughtRewards = "onslaught-rewards";
    public const string EventDefinitions = "event-definitions";
    public const string EventOccurrences = "event-occurrences";

    // Route/key prefixes for the split dataset families.
    public const string UnitsPrefix = "units";
    public const string NpcsPrefix = "npcs";
    public const string CampaignBattlesPrefix = "campaign-battles";
    public const string EquipmentPrefix = "equipment";
    public const string UpgradesPrefix = "upgrades";
    public const string LresPrefix = "lres";

    /// <summary>Unit (characters + MoWs) datasets, one per faction. Key = <c>units-{factionSlug}</c>.</summary>
    public static readonly IReadOnlyList<string> UnitFactions =
    [
        "units-adeptusastartes",
        "units-adeptusmechanicus",
        "units-aeldari",
        "units-astramilitarum",
        "units-blacklegion",
        "units-blacktemplars",
        "units-bloodangels",
        "units-custodes",
        "units-darkangels",
        "units-deathguard",
        "units-emperorschildren",
        "units-genestealers",
        "units-leaguesofvotann",
        "units-necrons",
        "units-orks",
        "units-sisterhood",
        "units-spacewolves",
        "units-tau",
        "units-thousandsons",
        "units-tyranids",
        "units-ultramarines",
        "units-worldeaters",
    ];

    /// <summary>NPC datasets, one per faction plus the faction-less <c>npcs-objects</c> bucket.</summary>
    public static readonly IReadOnlyList<string> NpcFactions =
    [
        "npcs-adeptusastartes",
        "npcs-adeptusmechanicus",
        "npcs-aeldari",
        "npcs-astramilitarum",
        "npcs-blacklegion",
        "npcs-blacktemplars",
        "npcs-bloodangels",
        "npcs-darkangels",
        "npcs-deathguard",
        "npcs-genestealers",
        "npcs-leaguesofvotann",
        "npcs-necrons",
        "npcs-orks",
        "npcs-sisterhood",
        "npcs-spacewolves",
        "npcs-tau",
        "npcs-thousandsons",
        "npcs-tyranids",
        "npcs-ultramarines",
        "npcs-worldeaters",
        "npcs-objects",
    ];

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

    /// <summary>Equipment datasets, one per item type.</summary>
    public static readonly IReadOnlyList<string> EquipmentTypes =
    [
        "equipment-crit",
        "equipment-booster-crit",
        "equipment-defensive",
        "equipment-block",
        "equipment-booster-block",
    ];

    /// <summary>
    /// Raw upgrade-material source datasets: one base (non-craftable) file per rarity plus a
    /// per-rarity crafted file for the rarities that have craftable items (Common has none).
    /// </summary>
    public static readonly IReadOnlyList<string> UpgradeRarities =
    [
        "upgrades-common",
        "upgrades-uncommon",
        "upgrades-uncommon-crafted",
        "upgrades-rare",
        "upgrades-rare-crafted",
        "upgrades-epic",
        "upgrades-epic-crafted",
        "upgrades-legendary",
        "upgrades-legendary-crafted",
        "upgrades-mythic",
        "upgrades-mythic-crafted",
    ];

    /// <summary>Legendary release event datasets, one per active event (keyed by unit snowprintId).</summary>
    public static readonly IReadOnlyList<string> LreEvents =
    [
        "lres-emperlucius",
        "lres-taufarsight",
        "lres-votanuthar",
    ];

    /// <summary>Raw embedded source datasets (one per file) used to build the served catalog.</summary>
    public static readonly IReadOnlyList<string> Required =
    [
        .. UnitFactions,
        MowUpgradeCosts,
        EquipmentUpgradeCosts,
        DropChances,
        AscensionCosts,
        UnlockShardCosts,
        OnslaughtRewards,
        EventDefinitions,
        EventOccurrences,
        .. NpcFactions,
        .. EquipmentTypes,
        .. UpgradeRarities,
        .. CampaignBattleGroups,
        .. LreEvents,
    ];

    // Served (denormalized) dataset keys — the public manifest surface. Each is one consolidated,
    // self-contained dataset computed from the raw source (reference tables inlined; no client joins).
    public const string Characters = "characters";
    public const string Npcs = "npcs";
    public const string Mows = "mows";
    // The shared mow upgrade-cost ladder, served as its own dataset (it is a single progression shared by
    // every mow, so it is not inlined per record).
    public const string MowUpgradeCostsServed = MowUpgradeCosts;
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
    // bulky per-battle wave data (keyed "{lreId}-{track}-{number}"); lre-common is the single shared reward
    // ladder (identical across every event).
    public const string Lres = "lres";
    public const string LreBattles = "lre-battles";
    public const string LreCommon = "lre-common";
    // event-definitions is served as-is (structural rules only, no denormalization needed beyond the
    // raw->view pass-through). events-calendar is the denormalized, date-indexed projection built from
    // event-definitions + event-occurrences (see Denormalization/EventsDenormalizer.cs); raw
    // event-occurrences itself is never served directly — see design.md Decision 1 in
    // add-game-events-calendar-dataset.
    public const string EventDefinitionsServed = EventDefinitions;
    public const string EventsCalendar = "events-calendar";

    /// <summary>The denormalized datasets exposed by the manifest / served by the catalog endpoints.</summary>
    public static readonly IReadOnlyList<string> Served =
    [
        Characters,
        Npcs,
        Mows,
        MowUpgradeCostsServed,
        AscensionCostsServed,
        UnlockShardCostsServed,
        OnslaughtRewards,
        Upgrades,
        Equipment,
        CampaignBattles,
        CampaignDefinitions,
        Lres,
        LreBattles,
        LreCommon,
        EventDefinitionsServed,
        EventsCalendar,
    ];
}
