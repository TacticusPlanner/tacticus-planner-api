namespace TacticusPlanner.GameCatalog.Models;

public static class GameCatalogDatasets
{
    /// <summary>Public route prefix the served datasets are mounted under (used to build manifest urls).</summary>
    public const string RoutePrefix = "/api/v1/game-catalog";

    public const string MowUpgradeCosts = "mow-upgrade-costs";
    public const string EquipmentUpgradeCosts = "equipment-upgrade-costs";
    public const string DropChances = "drop-chances";

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

    /// <summary>Campaign-battle datasets grouped by core characters (storyline x side).</summary>
    public static readonly IReadOnlyList<string> CampaignBattleGroups =
    [
        "campaign-battles-indomitus",
        "campaign-battles-indomitus-mirror",
        "campaign-battles-fall-of-cadia",
        "campaign-battles-fall-of-cadia-mirror",
        "campaign-battles-octarius",
        "campaign-battles-octarius-mirror",
        "campaign-battles-saim-hann",
        "campaign-battles-saim-hann-mirror",
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

    /// <summary>The denormalized datasets exposed by the manifest / served by the catalog endpoints.</summary>
    public static readonly IReadOnlyList<string> Served =
    [
        Characters,
        Npcs,
        Mows,
        MowUpgradeCostsServed,
        Upgrades,
        Equipment,
        CampaignBattles,
        CampaignDefinitions,
        Lres,
        LreBattles,
        LreCommon,
    ];
}
