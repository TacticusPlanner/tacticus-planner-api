using System.Collections.ObjectModel;
using System.Text.Json;

namespace TacticusPlanner.Catalog;

public static class CatalogDatasets
{
    public const string Units = "units";
    public const string Mows = "mows";
    public const string Upgrades = "upgrades";
    public const string Equipment = "equipment";
    public const string Campaigns = "campaigns";
    public const string CampaignEvents = "campaign-events";
    public const string CampaignBattles = "campaign-battles";
    public const string Lres = "lres";

    public static readonly IReadOnlyList<string> Required =
    [
        Units,
        Mows,
        Upgrades,
        Equipment,
        Campaigns,
        CampaignEvents,
        CampaignBattles,
        Lres,
    ];
}

public sealed record CatalogManifest(
    string Version,
    int SchemaVersion,
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
    string SourceHash,
    IReadOnlyDictionary<string, string> DatasetHashes,
    IReadOnlyList<CatalogUnit> Units,
    IReadOnlyList<CatalogMow> Mows,
    IReadOnlyList<CatalogMowUpgradeCost> MowUpgradeCosts,
    IReadOnlyList<CatalogUpgrade> Upgrades,
    IReadOnlyList<CatalogEquipment> Equipment,
    IReadOnlyList<CatalogCampaign> Campaigns,
    IReadOnlyList<CatalogCampaign> CampaignEvents,
    IReadOnlyList<CatalogCampaignBattle> CampaignBattles,
    IReadOnlyList<CatalogLre> Lres
)
{
    public IReadOnlyDictionary<string, CatalogUnit> UnitsById { get; } = ToLookup(Units, unit => unit.Id);

    public IReadOnlyDictionary<string, CatalogMow> MowsById { get; } = ToLookup(Mows, mow => mow.Id);

    public IReadOnlyDictionary<string, CatalogUpgrade> UpgradesById { get; } = ToLookup(Upgrades, upgrade => upgrade.Id);

    public IReadOnlyDictionary<string, CatalogEquipment> EquipmentById { get; } = ToLookup(Equipment, item => item.Id);

    public IReadOnlyDictionary<string, CatalogCampaign> CampaignsById { get; } = ToLookup(Campaigns, campaign => campaign.Id);

    public IReadOnlyDictionary<string, CatalogCampaignBattle> CampaignBattlesById { get; } =
        ToLookup(CampaignBattles, battle => battle.Id);

    public IReadOnlyDictionary<string, IReadOnlyList<string>> UpgradeFarmLocations { get; } =
        BuildUpgradeFarmLocations(CampaignBattles);

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> ExpandedUpgradeRecipes { get; } =
        BuildExpandedUpgradeRecipes(Upgrades);

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
            foreach (var reward in battle.Rewards.AllRewards)
            {
                if (string.IsNullOrWhiteSpace(reward.Id) || string.Equals(reward.Id, "gold", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!locations.TryGetValue(reward.Id, out var battleIds))
                {
                    battleIds = [];
                    locations[reward.Id] = battleIds;
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

public sealed record CatalogUnit(
    string Id,
    string Name,
    string UnitKind,
    string Title,
    string FullName,
    string ShortName,
    string ExtraShortName,
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
    bool RequiredInCampaign,
    IReadOnlyList<string> CampaignsRequiredIn,
    string? ReleaseDate
);

public sealed record CatalogMowsDataset(
    IReadOnlyList<CatalogMow> Mows,
    IReadOnlyList<CatalogMowUpgradeCost> UpgradeCosts
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

public sealed record CatalogCampaign(
    string Id,
    string Name,
    string DisplayName,
    string Faction,
    IReadOnlyList<string> CoreCharacters,
    string ReleaseType,
    string GroupType,
    string Difficulty
);

public sealed record CatalogCampaignBattle(
    string Id,
    string Campaign,
    string CampaignType,
    int EnergyCost,
    int NodeNumber,
    int Slots,
    IReadOnlyList<string> RequiredCharacterSnowprintIds,
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
    IReadOnlyList<CatalogCampaignReward> Guaranteed,
    IReadOnlyList<CatalogCampaignReward> Potential
)
{
    public IEnumerable<CatalogCampaignReward> AllRewards => Guaranteed.Concat(Potential);
}

public sealed record CatalogCampaignReward(
    string Id,
    int? Min,
    int? Max
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
    IReadOnlyList<CatalogLreRestriction> UnitsRestrictions
);

public sealed record CatalogLreTrackEnemies(
    string Label,
    string Link
);

public sealed record CatalogLreRestriction(
    string Name,
    int Points,
    string? IconId
);
