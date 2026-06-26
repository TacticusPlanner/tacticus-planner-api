using System.Collections.ObjectModel;
using System.Text.Json;

namespace TacticusPlanner.GameCatalog;

public static class GameCatalogDatasets
{
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
    public const string Lres = "lres";

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
    ];
}

public sealed record GameCatalogManifest(
    string Version,
    int SchemaVersion,
    string GameVersion,
    IReadOnlyList<GameCatalogDatasetMetadata> Datasets
);

public sealed record GameCatalogDatasetMetadata(
    string Key,
    string File,
    string Hash
);

public sealed record GameCatalogSnapshot(
    string Version,
    int SchemaVersion,
    string GameVersion,
    string SourceHash,
    IReadOnlyDictionary<string, string> DatasetHashes,
    // Raw source collections — kept for validation and the existing derived views.
    IReadOnlyDictionary<string, GameCatalogFactionUnits> UnitsByFaction,
    IReadOnlyList<GameCatalogMowUpgradeCost> MowUpgradeCosts,
    IReadOnlyList<GameCatalogEquipmentUpgradeCost> EquipmentUpgradeCosts,
    IReadOnlyDictionary<string, GameCatalogFactionNpcs> NpcsByFaction,
    IReadOnlyDictionary<string, IReadOnlyList<GameCatalogEquipment>> EquipmentByType,
    IReadOnlyDictionary<string, IReadOnlyList<GameCatalogUpgrade>> UpgradesByRarity,
    IReadOnlyDictionary<string, GameCatalogCampaignGroup> CampaignGroups,
    IReadOnlyList<GameCatalogDropChance> DropChances,
    IReadOnlyDictionary<string, GameCatalogLre> LresByEvent,
    // Served (denormalized) payloads — the public catalog surface.
    IReadOnlyList<GameCatalogCharacterView> CharacterViews,
    IReadOnlyList<GameCatalogNpc> NpcList,
    IReadOnlyList<GameCatalogMow> MowList,
    IReadOnlyList<GameCatalogMowUpgradeCostView> MowUpgradeCostViews,
    IReadOnlyList<GameCatalogUpgradeView> UpgradeViews,
    IReadOnlyList<GameCatalogEquipmentView> EquipmentViews,
    IReadOnlyList<GameCatalogCampaignBattleView> CampaignBattleViews,
    IReadOnlyList<GameCatalogCampaignDefinitionView> CampaignDefinitionViews,
    IReadOnlyList<GameCatalogLreView> LreViews
)
{
    // Flat views over the split datasets, so derived lookups and validation operate on the whole
    // catalog regardless of how the data is chunked across files. Each references constructor
    // parameters (never another computed property — that is illegal in a field initializer).
    public IReadOnlyList<GameCatalogCharacter> Characters { get; } =
        UnitsByFaction.Values.SelectMany(faction => faction.Characters).ToArray();

    public IReadOnlyList<GameCatalogMow> Mows { get; } =
        UnitsByFaction.Values.SelectMany(faction => faction.Mows).ToArray();

    public IReadOnlyList<GameCatalogNpc> Npcs { get; } =
        NpcsByFaction.Values.SelectMany(faction => faction.Npcs).ToArray();

    public IReadOnlyList<GameCatalogEquipment> Equipment { get; } =
        EquipmentByType.Values.SelectMany(items => items).ToArray();

    public IReadOnlyList<GameCatalogUpgrade> Upgrades { get; } =
        UpgradesByRarity.Values.SelectMany(items => items).ToArray();

    public IReadOnlyList<GameCatalogCampaignBattle> CampaignBattles { get; } =
        CampaignGroups.Values.SelectMany(group => group.Battles).ToArray();

    public IReadOnlyList<GameCatalogLre> Lres { get; } = LresByEvent.Values.ToArray();

    public IReadOnlyDictionary<string, GameCatalogCharacter> UnitsById { get; } =
        ToLookup(UnitsByFaction.Values.SelectMany(faction => faction.Characters), unit => unit.Id);

    public IReadOnlyDictionary<string, GameCatalogMow> MowsById { get; } =
        ToLookup(UnitsByFaction.Values.SelectMany(faction => faction.Mows), mow => mow.Id);

    public IReadOnlyDictionary<string, GameCatalogUpgrade> UpgradesById { get; } =
        ToLookup(UpgradesByRarity.Values.SelectMany(items => items), upgrade => upgrade.Id);

    public IReadOnlyDictionary<string, GameCatalogEquipment> EquipmentById { get; } =
        ToLookup(EquipmentByType.Values.SelectMany(items => items), item => item.Id);

    public IReadOnlyDictionary<string, GameCatalogCampaignBattle> CampaignBattlesById { get; } =
        ToLookup(CampaignGroups.Values.SelectMany(group => group.Battles), battle => battle.Id);

    public IReadOnlyDictionary<string, IReadOnlyList<string>> UpgradeFarmLocations { get; } =
        BuildUpgradeFarmLocations(CampaignGroups.Values.SelectMany(group => group.Battles).ToArray());

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> ExpandedUpgradeRecipes { get; } =
        BuildExpandedUpgradeRecipes(UpgradesByRarity.Values.SelectMany(items => items).ToArray());

    private static ReadOnlyDictionary<string, T> ToLookup<T>(
        IEnumerable<T> values,
        Func<T, string> keySelector
    )
    {
        var dictionary = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values)
        {
            var key = keySelector(value);
            if (!string.IsNullOrWhiteSpace(key) && !dictionary.ContainsKey(key))
            {
                dictionary[key] = value;
            }
        }

        return new ReadOnlyDictionary<string, T>(dictionary);
    }

    private static ReadOnlyDictionary<string, IReadOnlyList<string>> BuildUpgradeFarmLocations(
        IReadOnlyList<GameCatalogCampaignBattle> battles
    )
    {
        var locations = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var battle in battles)
        {
            foreach (var rewardId in battle.Rewards.AllRewardIds)
            {
                if (string.IsNullOrWhiteSpace(rewardId) || string.Equals(rewardId, "gold", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!locations.TryGetValue(rewardId, out var battleIds))
                {
                    battleIds = [];
                    locations[rewardId] = battleIds;
                }

                if (!battleIds.Contains(battle.Id, StringComparer.OrdinalIgnoreCase))
                {
                    battleIds.Add(battle.Id);
                }
            }
        }

        return new ReadOnlyDictionary<string, IReadOnlyList<string>>(
            locations.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase
            )
        );
    }

    private static ReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> BuildExpandedUpgradeRecipes(
        IReadOnlyList<GameCatalogUpgrade> upgrades
    )
    {
        var byId = upgrades.ToDictionary(upgrade => upgrade.Id, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var upgrade in upgrades)
        {
            var expanded = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            ExpandUpgrade(upgrade.Id, 1, byId, expanded, []);
            result[upgrade.Id] = new ReadOnlyDictionary<string, int>(expanded);
        }

        return new ReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>(result);
    }

    private static void ExpandUpgrade(
        string upgradeId,
        int multiplier,
        IReadOnlyDictionary<string, GameCatalogUpgrade> upgradesById,
        IDictionary<string, int> output,
        HashSet<string> stack
    )
    {
        if (!upgradesById.TryGetValue(upgradeId, out var upgrade) || !upgrade.Craftable || upgrade.Recipe.Count == 0)
        {
            output[upgradeId] = output.TryGetValue(upgradeId, out var current)
                ? current + multiplier
                : multiplier;
            return;
        }

        if (!stack.Add(upgradeId))
        {
            output[upgradeId] = output.TryGetValue(upgradeId, out var current)
                ? current + multiplier
                : multiplier;
            return;
        }

        foreach (var ingredient in upgrade.Recipe)
        {
            ExpandUpgrade(ingredient.Material, multiplier * ingredient.Count, upgradesById, output, stack);
        }

        stack.Remove(upgradeId);
    }
}

public sealed record GameCatalogFactionUnits(
    string Alliance,
    string FactionId,
    string Name,
    IReadOnlyList<GameCatalogCharacter> Characters,
    IReadOnlyList<GameCatalogMow> Mows
);

public sealed record GameCatalogCharacter(
    string Id,
    string Name,
    int Health,
    int Damage,
    int Armour,
    string InitialRarity,
    string MeleeDamage,
    int MeleeHits,
    string? RangedDamage,
    int? RangedHits,
    int? RangeDistance,
    int Movement,
    IReadOnlyList<string> Traits,
    IReadOnlyList<string> ActiveAbilityNames,
    IReadOnlyList<string> PassiveAbilityNames,
    IReadOnlyList<string> EquipmentSlots,
    string Icon,
    string RoundIcon,
    IReadOnlyList<GameCatalogCharacterRankUp> RankUpUpgrades
);

public sealed record GameCatalogCharacterRankUp(
    string Rank,
    IReadOnlyList<string> UpgradeIds
);

public sealed record GameCatalogMow(
    string Id,
    string Name,
    string UnitKind,
    string Faction,
    string Alliance,
    string Icon,
    string RoundIcon,
    GameCatalogMowAbility PrimaryAbility,
    GameCatalogMowAbility SecondaryAbility
);

public sealed record GameCatalogMowAbility(
    string Name,
    IReadOnlyList<IReadOnlyList<string>> Recipes
);

public sealed record GameCatalogMowUpgradeCost(
    int Gold,
    int Salvage,
    GameCatalogAmountByRarity Badges,
    GameCatalogAmountByRarity? ForgeBadges,
    int Components
);

// The served projection of a mow upgrade-cost rung, keyed by the ability level it raises a MoW to. The
// raw ladder is a flat array (cost[0] = level 2 … cost[n] = level n+2), so Level correlates the rung with
// the in-game ability level rather than an opaque array index.
public sealed record GameCatalogMowUpgradeCostView(
    int Level,
    int Gold,
    int Salvage,
    GameCatalogAmountByRarity Badges,
    GameCatalogAmountByRarity? ForgeBadges,
    int Components
);

public sealed record GameCatalogAmountByRarity(
    string Rarity,
    int Amount
);

public sealed record GameCatalogEquipmentUpgradeCost(
    string Rarity,
    IReadOnlyList<GameCatalogEquipmentUpgradeLevel> Levels
);

public sealed record GameCatalogEquipmentUpgradeLevel(
    int GoldCost,
    int SalvageCost,
    int MythicSalvageCost
);

public sealed record GameCatalogUpgrade(
    string Id,
    string Material,
    string SnowprintId,
    string Label,
    string Rarity,
    string Stat,
    string? Icon,
    bool Craftable,
    IReadOnlyList<GameCatalogUpgradeRecipeIngredient> Recipe
);

public sealed record GameCatalogUpgradeRecipeIngredient(
    string Material,
    int Count,
    // Populated server-side for craftable ingredients: the ingredient's own recipe, nested recursively.
    // Null for base (non-craftable) materials. Absent in the raw source JSON (which is flat).
    IReadOnlyList<GameCatalogUpgradeRecipeIngredient>? Recipe = null
);

public sealed record GameCatalogEquipment(
    string Id,
    string Name,
    string Rarity,
    string Type,
    string? AbilityId,
    bool IsRelic,
    bool IsUniqueRelic,
    IReadOnlyList<string> AllowedUnits,
    IReadOnlyList<string> AllowedFactions,
    IReadOnlyList<JsonElement> Levels
);

public sealed record GameCatalogCampaignGroup(
    string GroupId,
    string Faction,
    string ReleaseType,
    IReadOnlyList<string> CoreCharacters,
    IReadOnlyList<string> Difficulties,
    IReadOnlyList<GameCatalogCampaignBattle> Battles
);

public sealed record GameCatalogCampaignBattle(
    string Id,
    string Difficulty,
    int EnergyCost,
    int NodeNumber,
    int Slots,
    GameCatalogCampaignRewards Rewards,
    int EnemyPower,
    IReadOnlyList<string> EnemiesAlliances,
    IReadOnlyList<string> EnemiesFactions,
    int EnemiesTotal,
    IReadOnlyList<string> EnemiesTypes,
    IReadOnlyList<JsonElement> RawEnemyTypes,
    IReadOnlyList<GameCatalogCampaignDetailedEnemy> DetailedEnemyTypes
);

public sealed record GameCatalogCampaignRewards(
    IReadOnlyList<GameCatalogCampaignGuaranteedReward> Guaranteed,
    IReadOnlyList<GameCatalogCampaignPotentialReward> Potential
)
{
    public IEnumerable<string> AllRewardIds =>
        Guaranteed.Select(reward => reward.Id).Concat(Potential.Select(reward => reward.Id));
}

public sealed record GameCatalogCampaignGuaranteedReward(
    string Id,
    int? Min,
    int? Max
);

public sealed record GameCatalogCampaignPotentialReward(
    string Id,
    string ChanceId
);

public sealed record GameCatalogDropChance(
    string Id,
    string RewardKind,
    string Difficulty,
    int Numerator,
    int Denominator,
    double EffectiveRate
);

public sealed record GameCatalogCampaignDetailedEnemy(
    string Id,
    string Name,
    int Count,
    int Stars,
    string Rank
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
    IReadOnlyList<JsonElement> PointsMilestones,
    IReadOnlyList<JsonElement> ChestsMilestones,
    int ShardsPerChest,
    JsonElement Progression
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

public sealed record GameCatalogFactionNpcs(
    string Alliance,
    string FactionId,
    string Name,
    IReadOnlyList<GameCatalogNpc> Npcs
);

public sealed record GameCatalogNpc(
    string Id,
    string Name,
    string MeleeDamage,
    int MeleeHits,
    string? RangedDamage,
    int? RangedHits,
    int? Distance,
    int Movement,
    IReadOnlyList<string> Traits,
    IReadOnlyList<string> ActiveAbilityDamage,
    IReadOnlyList<string> ActiveAbilities,
    IReadOnlyList<string> PassiveAbilityDamage,
    IReadOnlyList<string> PassiveAbilities,
    string? Icon,
    IReadOnlyList<GameCatalogNpcStat> Stats
);

public sealed record GameCatalogNpcStat(
    int AbilityLevel,
    int Damage,
    int Armour,
    int Health,
    int ProgressionIndex,
    int Rank,
    int Stars
);

// ---- Denormalized served views ---------------------------------------------------------------
// A campaign-battle location that drops a reward (a character shard or an upgrade material), with the
// drop chance inlined. Guaranteed rewards carry Guaranteed=true and no rate; potential rewards carry the
// resolved drop-chance numbers.
public sealed record GameCatalogFarmLocation(
    string BattleId,
    string Difficulty,
    bool Guaranteed,
    string? ChanceId,
    int? Numerator,
    int? Denominator,
    double? EffectiveRate
);

public sealed record GameCatalogEquipmentSlot(
    string Slot,
    IReadOnlyList<string> EquipmentIds
);

public sealed record GameCatalogCharacterView(
    string Id,
    string Name,
    string Faction,
    string Alliance,
    int Health,
    int Damage,
    int Armour,
    string InitialRarity,
    string MeleeDamage,
    int MeleeHits,
    string? RangedDamage,
    int? RangedHits,
    int? RangeDistance,
    int Movement,
    IReadOnlyList<string> Traits,
    IReadOnlyList<string> ActiveAbilityNames,
    IReadOnlyList<string> PassiveAbilityNames,
    IReadOnlyList<string> EquipmentSlots,
    string Icon,
    string RoundIcon,
    IReadOnlyList<GameCatalogCharacterRankUp> RankUpUpgrades,
    IReadOnlyList<GameCatalogFarmLocation> ShardLocations,
    IReadOnlyList<GameCatalogEquipmentSlot> EligibleEquipment
);

public sealed record GameCatalogUpgradeView(
    string Id,
    string Material,
    string SnowprintId,
    string Label,
    string Rarity,
    string Stat,
    string? Icon,
    bool Craftable,
    // For craftable upgrades each ingredient carries its own nested recipe (recursively), so the client
    // can walk the full crafting tree without a separate expansion table.
    IReadOnlyList<GameCatalogUpgradeRecipeIngredient> Recipe,
    IReadOnlyList<GameCatalogFarmLocation> FarmLocations
);

// Equipment with its per-rarity upgrade-cost ladder inlined (the matched rarity's levels), so the client
// never joins against a shared cost table.
public sealed record GameCatalogEquipmentView(
    string Id,
    string Name,
    string Rarity,
    string Type,
    string? AbilityId,
    bool IsRelic,
    bool IsUniqueRelic,
    IReadOnlyList<string> AllowedUnits,
    IReadOnlyList<string> AllowedFactions,
    IReadOnlyList<JsonElement> Levels,
    IReadOnlyList<GameCatalogEquipmentUpgradeLevel> UpgradeLevels
);

public sealed record GameCatalogCampaignPotentialRewardView(
    string Id,
    string? ChanceId,
    string? RewardKind,
    int? Numerator,
    int? Denominator,
    double? EffectiveRate
);

public sealed record GameCatalogCampaignRewardsView(
    IReadOnlyList<GameCatalogCampaignGuaranteedReward> Guaranteed,
    IReadOnlyList<GameCatalogCampaignPotentialRewardView> Potential
);

public sealed record GameCatalogCampaignBattleView(
    string Id,
    string CampaignGroupId,
    string Difficulty,
    int EnergyCost,
    int NodeNumber,
    int Slots,
    GameCatalogCampaignRewardsView Rewards,
    int EnemyPower,
    IReadOnlyList<string> EnemiesAlliances,
    IReadOnlyList<string> EnemiesFactions,
    int EnemiesTotal,
    IReadOnlyList<string> EnemiesTypes,
    IReadOnlyList<GameCatalogCampaignDetailedEnemy> DetailedEnemyTypes
);

// One campaign group's definition: its metadata plus the ids of the battles that belong to it (the
// battle bodies live in the campaign-battles dataset, keyed by battle id).
public sealed record GameCatalogCampaignDefinitionView(
    string GroupId,
    string Faction,
    string ReleaseType,
    IReadOnlyList<string> CoreCharacters,
    IReadOnlyList<string> Difficulties,
    IReadOnlyList<string> BattleIds
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
    IReadOnlyList<JsonElement> PointsMilestones,
    IReadOnlyList<JsonElement> ChestsMilestones,
    int ShardsPerChest,
    JsonElement Progression
);
