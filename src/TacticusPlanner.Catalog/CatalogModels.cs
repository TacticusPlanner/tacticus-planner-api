using System.Collections.ObjectModel;
using System.Text.Json;

namespace TacticusPlanner.Catalog;

public static class CatalogDatasets
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
    public const string Upgrades = "upgrades";
    public const string Equipment = "equipment";
    public const string CampaignBattles = "campaign-battles";
    public const string Lres = "lres";

    /// <summary>The denormalized datasets exposed by the manifest / served by the catalog endpoints.</summary>
    public static readonly IReadOnlyList<string> Served =
    [
        Characters,
        Npcs,
        Mows,
        Upgrades,
        Equipment,
        CampaignBattles,
        Lres,
    ];
}

public sealed record CatalogManifest(
    string Version,
    int SchemaVersion,
    string GameVersion,
    IReadOnlyList<CatalogDatasetMetadata> Datasets
);

public sealed record CatalogDatasetMetadata(
    string Key,
    string File,
    string Hash
);

public sealed record CatalogSnapshot(
    string Version,
    int SchemaVersion,
    string GameVersion,
    string SourceHash,
    IReadOnlyDictionary<string, string> DatasetHashes,
    // Raw source collections — kept for validation and the existing derived views.
    IReadOnlyDictionary<string, CatalogFactionUnits> UnitsByFaction,
    IReadOnlyList<CatalogMowUpgradeCost> MowUpgradeCosts,
    IReadOnlyList<CatalogEquipmentUpgradeCost> EquipmentUpgradeCosts,
    IReadOnlyDictionary<string, CatalogFactionNpcs> NpcsByFaction,
    IReadOnlyDictionary<string, IReadOnlyList<CatalogEquipment>> EquipmentByType,
    IReadOnlyDictionary<string, IReadOnlyList<CatalogUpgrade>> UpgradesByRarity,
    IReadOnlyDictionary<string, CatalogCampaignGroup> CampaignGroups,
    IReadOnlyList<CatalogDropChance> DropChances,
    IReadOnlyDictionary<string, CatalogLre> LresByEvent,
    // Served (denormalized) payloads — the public catalog surface.
    IReadOnlyList<CatalogCharacterView> CharacterViews,
    IReadOnlyList<CatalogNpc> NpcList,
    CatalogMowDataset MowDataset,
    IReadOnlyList<CatalogUpgradeView> UpgradeViews,
    CatalogEquipmentDataset EquipmentDataset,
    IReadOnlyList<CatalogCampaignGroupView> CampaignGroupViews,
    IReadOnlyList<CatalogLreView> LreViews
)
{
    // Flat views over the split datasets, so derived lookups and validation operate on the whole
    // catalog regardless of how the data is chunked across files. Each references constructor
    // parameters (never another computed property — that is illegal in a field initializer).
    public IReadOnlyList<CatalogCharacter> Characters { get; } =
        UnitsByFaction.Values.SelectMany(faction => faction.Characters).ToArray();

    public IReadOnlyList<CatalogMow> Mows { get; } =
        UnitsByFaction.Values.SelectMany(faction => faction.Mows).ToArray();

    public IReadOnlyList<CatalogNpc> Npcs { get; } =
        NpcsByFaction.Values.SelectMany(faction => faction.Npcs).ToArray();

    public IReadOnlyList<CatalogEquipment> Equipment { get; } =
        EquipmentByType.Values.SelectMany(items => items).ToArray();

    public IReadOnlyList<CatalogUpgrade> Upgrades { get; } =
        UpgradesByRarity.Values.SelectMany(items => items).ToArray();

    public IReadOnlyList<CatalogCampaignBattle> CampaignBattles { get; } =
        CampaignGroups.Values.SelectMany(group => group.Battles).ToArray();

    public IReadOnlyList<CatalogLre> Lres { get; } = LresByEvent.Values.ToArray();

    public IReadOnlyDictionary<string, CatalogCharacter> UnitsById { get; } =
        ToLookup(UnitsByFaction.Values.SelectMany(faction => faction.Characters), unit => unit.Id);

    public IReadOnlyDictionary<string, CatalogMow> MowsById { get; } =
        ToLookup(UnitsByFaction.Values.SelectMany(faction => faction.Mows), mow => mow.Id);

    public IReadOnlyDictionary<string, CatalogUpgrade> UpgradesById { get; } =
        ToLookup(UpgradesByRarity.Values.SelectMany(items => items), upgrade => upgrade.Id);

    public IReadOnlyDictionary<string, CatalogEquipment> EquipmentById { get; } =
        ToLookup(EquipmentByType.Values.SelectMany(items => items), item => item.Id);

    public IReadOnlyDictionary<string, CatalogCampaignBattle> CampaignBattlesById { get; } =
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
        IReadOnlyList<CatalogCampaignBattle> battles
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
        IReadOnlyList<CatalogUpgrade> upgrades
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
        IReadOnlyDictionary<string, CatalogUpgrade> upgradesById,
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

public sealed record CatalogFactionUnits(
    string Alliance,
    string FactionId,
    string Name,
    IReadOnlyList<CatalogCharacter> Characters,
    IReadOnlyList<CatalogMow> Mows
);

public sealed record CatalogCharacter(
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
    IReadOnlyList<CatalogCharacterRankUp> RankUpUpgrades
);

public sealed record CatalogCharacterRankUp(
    string Rank,
    IReadOnlyList<string> UpgradeIds
);

public sealed record CatalogMow(
    string Id,
    string Name,
    string UnitKind,
    string Faction,
    string Alliance,
    string Icon,
    string RoundIcon,
    CatalogMowAbility PrimaryAbility,
    CatalogMowAbility SecondaryAbility
);

public sealed record CatalogMowAbility(
    string Name,
    IReadOnlyList<IReadOnlyList<string>> Recipes
);

public sealed record CatalogMowUpgradeCost(
    int Gold,
    int Salvage,
    CatalogAmountByRarity Badges,
    CatalogAmountByRarity? ForgeBadges,
    int Components
);

public sealed record CatalogAmountByRarity(
    string Rarity,
    int Amount
);

public sealed record CatalogEquipmentUpgradeCost(
    string Rarity,
    IReadOnlyList<CatalogEquipmentUpgradeLevel> Levels
);

public sealed record CatalogEquipmentUpgradeLevel(
    int GoldCost,
    int SalvageCost,
    int MythicSalvageCost
);

public sealed record CatalogUpgrade(
    string Id,
    string Material,
    string SnowprintId,
    string Label,
    string Rarity,
    string Stat,
    string? Icon,
    bool Craftable,
    IReadOnlyList<CatalogUpgradeRecipeIngredient> Recipe
);

public sealed record CatalogUpgradeRecipeIngredient(
    string Material,
    int Count
);

public sealed record CatalogEquipment(
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

public sealed record CatalogCampaignGroup(
    string GroupId,
    string Faction,
    string ReleaseType,
    IReadOnlyList<string> CoreCharacters,
    IReadOnlyList<string> Difficulties,
    IReadOnlyList<CatalogCampaignBattle> Battles
);

public sealed record CatalogCampaignBattle(
    string Id,
    string Difficulty,
    int EnergyCost,
    int NodeNumber,
    int Slots,
    CatalogCampaignRewards Rewards,
    int EnemyPower,
    IReadOnlyList<string> EnemiesAlliances,
    IReadOnlyList<string> EnemiesFactions,
    int EnemiesTotal,
    IReadOnlyList<string> EnemiesTypes,
    IReadOnlyList<JsonElement> RawEnemyTypes,
    IReadOnlyList<CatalogCampaignDetailedEnemy> DetailedEnemyTypes
);

public sealed record CatalogCampaignRewards(
    IReadOnlyList<CatalogCampaignGuaranteedReward> Guaranteed,
    IReadOnlyList<CatalogCampaignPotentialReward> Potential
)
{
    public IEnumerable<string> AllRewardIds =>
        Guaranteed.Select(reward => reward.Id).Concat(Potential.Select(reward => reward.Id));
}

public sealed record CatalogCampaignGuaranteedReward(
    string Id,
    int? Min,
    int? Max
);

public sealed record CatalogCampaignPotentialReward(
    string Id,
    string ChanceId
);

public sealed record CatalogDropChance(
    string Id,
    string RewardKind,
    string Difficulty,
    int Numerator,
    int Denominator,
    double EffectiveRate
);

public sealed record CatalogCampaignDetailedEnemy(
    string Id,
    string Name,
    int Count,
    int Stars,
    string Rank
);

public sealed record CatalogLre(
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
    CatalogLreTrack Alpha,
    CatalogLreTrack Beta,
    CatalogLreTrack Gamma,
    IReadOnlyList<JsonElement> PointsMilestones,
    IReadOnlyList<JsonElement> ChestsMilestones,
    int ShardsPerChest,
    JsonElement Progression
);

public sealed record CatalogLreTrack(
    string Name,
    CatalogLreTrackEnemies Enemies,
    int KillPoints,
    IReadOnlyList<int> BattlesPoints,
    IReadOnlyList<int> DefeatAll,
    IReadOnlyList<CatalogLreFilter> AllowedUnitsFilter,
    IReadOnlyList<CatalogLreRestriction> UnitsRestrictions,
    IReadOnlyList<CatalogLreBattle> Battles
);

public sealed record CatalogLreBattle(
    string MapId,
    int Number,
    int Power,
    int Tier,
    IReadOnlyList<string> DisallowedFactions,
    IReadOnlyList<CatalogLreWave> Waves
);

public sealed record CatalogLreWave(
    int Round,
    int Power,
    IReadOnlyList<CatalogLreEnemy> Enemies
);

public sealed record CatalogLreEnemy(
    string Id,
    int Stars,
    int Count
);

public sealed record CatalogLreTrackEnemies(
    string Label,
    string Link
);

public sealed record CatalogLreRestriction(
    string Name,
    int Points,
    string? IconId,
    int Index,
    CatalogLreFilter Filter
);

public sealed record CatalogLreFilter(
    string Kind,
    string Target,
    bool Exclude
);

public sealed record CatalogFactionNpcs(
    string Alliance,
    string FactionId,
    string Name,
    IReadOnlyList<CatalogNpc> Npcs
);

public sealed record CatalogNpc(
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
    IReadOnlyList<CatalogNpcStat> Stats
);

public sealed record CatalogNpcStat(
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
public sealed record CatalogFarmLocation(
    string BattleId,
    string Difficulty,
    bool Guaranteed,
    string? ChanceId,
    int? Numerator,
    int? Denominator,
    double? EffectiveRate
);

public sealed record CatalogEquipmentSlot(
    string Slot,
    IReadOnlyList<string> EquipmentIds
);

public sealed record CatalogCharacterView(
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
    IReadOnlyList<CatalogCharacterRankUp> RankUpUpgrades,
    IReadOnlyList<CatalogFarmLocation> ShardLocations,
    IReadOnlyList<CatalogEquipmentSlot> EligibleEquipment
);

public sealed record CatalogMowDataset(
    IReadOnlyList<CatalogMow> Items,
    IReadOnlyList<CatalogMowUpgradeCost> UpgradeCosts
);

// Recursive recipe expansion split into base (non-craftable leaf) materials and the crafted
// intermediates consumed along the way, with running totals for each.
public sealed record CatalogUpgradeExpansion(
    IReadOnlyDictionary<string, int> BaseUpgrades,
    IReadOnlyDictionary<string, int> CraftedUpgrades,
    int TotalBaseCount,
    int TotalCraftedCount
);

public sealed record CatalogUpgradeView(
    string Id,
    string Material,
    string SnowprintId,
    string Label,
    string Rarity,
    string Stat,
    string? Icon,
    bool Craftable,
    IReadOnlyList<CatalogUpgradeRecipeIngredient> Recipe,
    IReadOnlyList<CatalogFarmLocation> FarmLocations,
    CatalogUpgradeExpansion? Expanded
);

public sealed record CatalogEquipmentDataset(
    IReadOnlyList<CatalogEquipment> Items,
    IReadOnlyList<CatalogEquipmentUpgradeCost> UpgradeCostsByRarity
);

public sealed record CatalogCampaignPotentialRewardView(
    string Id,
    string? ChanceId,
    string? RewardKind,
    int? Numerator,
    int? Denominator,
    double? EffectiveRate
);

public sealed record CatalogCampaignRewardsView(
    IReadOnlyList<CatalogCampaignGuaranteedReward> Guaranteed,
    IReadOnlyList<CatalogCampaignPotentialRewardView> Potential
);

public sealed record CatalogCampaignBattleView(
    string Id,
    string Difficulty,
    int EnergyCost,
    int NodeNumber,
    int Slots,
    CatalogCampaignRewardsView Rewards,
    int EnemyPower,
    IReadOnlyList<string> EnemiesAlliances,
    IReadOnlyList<string> EnemiesFactions,
    int EnemiesTotal,
    IReadOnlyList<string> EnemiesTypes,
    IReadOnlyList<CatalogCampaignDetailedEnemy> DetailedEnemyTypes
);

public sealed record CatalogCampaignGroupView(
    string GroupId,
    string Faction,
    string ReleaseType,
    IReadOnlyList<string> CoreCharacters,
    IReadOnlyList<string> Difficulties,
    IReadOnlyList<CatalogCampaignBattleView> Battles
);

public sealed record CatalogLreTrackView(
    string Name,
    CatalogLreTrackEnemies Enemies,
    int KillPoints,
    IReadOnlyList<int> BattlesPoints,
    IReadOnlyList<int> DefeatAll,
    IReadOnlyList<CatalogLreFilter> AllowedUnitsFilter,
    IReadOnlyList<CatalogLreRestriction> UnitsRestrictions,
    IReadOnlyList<CatalogLreBattle> Battles,
    IReadOnlyList<string> AvailableUnitIds
);

public sealed record CatalogLreView(
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
    CatalogLreTrackView Alpha,
    CatalogLreTrackView Beta,
    CatalogLreTrackView Gamma,
    IReadOnlyList<JsonElement> PointsMilestones,
    IReadOnlyList<JsonElement> ChestsMilestones,
    int ShardsPerChest,
    JsonElement Progression
);
