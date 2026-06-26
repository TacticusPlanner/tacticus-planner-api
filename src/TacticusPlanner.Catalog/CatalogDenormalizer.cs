using System.Collections.ObjectModel;

namespace TacticusPlanner.Catalog;

/// <summary>
/// Builds the consolidated, denormalized served datasets from the raw source collections: cross-references
/// (shard/upgrade farm locations with inlined drop chances, eligible equipment, recursively expanded
/// recipes, per-track available units) are resolved server-side so the client never joins.
/// </summary>
internal static class CatalogDenormalizer
{
    private static readonly string[] ShardPrefixes = ["shards_", "mythicShards_"];

    private readonly record struct RewardLocation(string BattleId, string Difficulty, bool Guaranteed, string? ChanceId);

    public static IReadOnlyList<CatalogCharacterView> BuildCharacters(
        IReadOnlyDictionary<string, CatalogFactionUnits> unitsByFaction,
        IReadOnlyDictionary<string, IReadOnlyList<CatalogEquipment>> equipmentByType,
        IReadOnlyDictionary<string, CatalogCampaignGroup> campaignGroups,
        IReadOnlyList<CatalogDropChance> dropChances)
    {
        var dropChanceById = BuildDropChanceIndex(dropChances);
        var rewardLocations = BuildRewardLocations(campaignGroups);
        var equipmentByTypeName = equipmentByType.Values
            .SelectMany(items => items)
            .GroupBy(item => item.Type, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var characters = new List<CatalogCharacterView>();
        foreach (var faction in unitsByFaction.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value))
        {
            foreach (var character in faction.Characters)
            {
                characters.Add(new CatalogCharacterView(
                    character.Id,
                    character.Name,
                    faction.FactionId,
                    faction.Alliance,
                    character.Health,
                    character.Damage,
                    character.Armour,
                    character.InitialRarity,
                    character.MeleeDamage,
                    character.MeleeHits,
                    character.RangedDamage,
                    character.RangedHits,
                    character.RangeDistance,
                    character.Movement,
                    character.Traits,
                    character.ActiveAbilityNames,
                    character.PassiveAbilityNames,
                    character.EquipmentSlots,
                    character.Icon,
                    character.RoundIcon,
                    character.RankUpUpgrades,
                    BuildShardLocations(character.Id, rewardLocations, dropChanceById),
                    BuildEligibleEquipment(character, faction.FactionId, equipmentByTypeName)));
            }
        }

        return characters;
    }

    public static IReadOnlyList<CatalogNpc> BuildNpcs(IReadOnlyDictionary<string, CatalogFactionNpcs> npcsByFaction) =>
        npcsByFaction
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => pair.Value.Npcs)
            .ToArray();

    public static CatalogMowDataset BuildMows(
        IReadOnlyDictionary<string, CatalogFactionUnits> unitsByFaction,
        IReadOnlyList<CatalogMowUpgradeCost> mowUpgradeCosts) =>
        new(
            unitsByFaction
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SelectMany(pair => pair.Value.Mows)
                .ToArray(),
            mowUpgradeCosts);

    public static IReadOnlyList<CatalogUpgradeView> BuildUpgrades(
        IReadOnlyDictionary<string, IReadOnlyList<CatalogUpgrade>> upgradesByRarity,
        IReadOnlyDictionary<string, CatalogCampaignGroup> campaignGroups,
        IReadOnlyList<CatalogDropChance> dropChances)
    {
        var dropChanceById = BuildDropChanceIndex(dropChances);
        var rewardLocations = BuildRewardLocations(campaignGroups);
        var upgrades = upgradesByRarity
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => pair.Value)
            .ToArray();
        var byId = upgrades.ToDictionary(upgrade => upgrade.Id, StringComparer.OrdinalIgnoreCase);

        var views = new List<CatalogUpgradeView>(upgrades.Length);
        foreach (var upgrade in upgrades)
        {
            views.Add(new CatalogUpgradeView(
                upgrade.Id,
                upgrade.Material,
                upgrade.SnowprintId,
                upgrade.Label,
                upgrade.Rarity,
                upgrade.Stat,
                upgrade.Icon,
                upgrade.Craftable,
                upgrade.Recipe,
                ResolveLocations(upgrade.Id, rewardLocations, dropChanceById),
                upgrade.Craftable && upgrade.Recipe.Count > 0 ? ExpandRecipe(upgrade.Id, byId) : null));
        }

        return views;
    }

    public static CatalogEquipmentDataset BuildEquipment(
        IReadOnlyDictionary<string, IReadOnlyList<CatalogEquipment>> equipmentByType,
        IReadOnlyList<CatalogEquipmentUpgradeCost> equipmentUpgradeCosts) =>
        new(
            equipmentByType
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SelectMany(pair => pair.Value)
                .ToArray(),
            equipmentUpgradeCosts);

    public static IReadOnlyList<CatalogCampaignGroupView> BuildCampaignGroups(
        IReadOnlyDictionary<string, CatalogCampaignGroup> campaignGroups,
        IReadOnlyList<CatalogDropChance> dropChances)
    {
        var dropChanceById = BuildDropChanceIndex(dropChances);

        return campaignGroups
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .Select(group => new CatalogCampaignGroupView(
                group.GroupId,
                group.Faction,
                group.ReleaseType,
                group.CoreCharacters,
                group.Difficulties,
                group.Battles.Select(battle => BuildBattleView(battle, dropChanceById)).ToArray()))
            .ToArray();
    }

    public static IReadOnlyList<CatalogLreView> BuildLres(
        IReadOnlyDictionary<string, CatalogLre> lresByEvent,
        IReadOnlyDictionary<string, CatalogFactionUnits> unitsByFaction)
    {
        var roster = unitsByFaction
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .SelectMany(faction => faction.Characters.Select(character =>
                (character.Id, Faction: faction.FactionId, faction.Alliance)))
            .ToArray();

        return lresByEvent
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .Select(lre => new CatalogLreView(
                lre.Id,
                lre.UnitSnowprintId,
                lre.Name,
                lre.WikiLink,
                lre.EventStage,
                lre.Finished,
                lre.NextEventDate,
                lre.NextEventDateUtc,
                lre.BattlesCount,
                lre.ConstraintsCount,
                lre.RegularMissions,
                lre.PremiumMissions,
                BuildTrackView(lre.Alpha, roster),
                BuildTrackView(lre.Beta, roster),
                BuildTrackView(lre.Gamma, roster),
                lre.PointsMilestones,
                lre.ChestsMilestones,
                lre.ShardsPerChest,
                lre.Progression))
            .ToArray();
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static Dictionary<string, CatalogDropChance> BuildDropChanceIndex(IReadOnlyList<CatalogDropChance> dropChances) =>
        dropChances
            .GroupBy(chance => chance.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    private static Dictionary<string, List<RewardLocation>> BuildRewardLocations(
        IReadOnlyDictionary<string, CatalogCampaignGroup> campaignGroups)
    {
        var locations = new Dictionary<string, List<RewardLocation>>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in campaignGroups.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value))
        {
            foreach (var battle in group.Battles)
            {
                foreach (var reward in battle.Rewards.Guaranteed)
                {
                    Add(reward.Id, new RewardLocation(battle.Id, battle.Difficulty, true, null));
                }

                foreach (var reward in battle.Rewards.Potential)
                {
                    Add(reward.Id, new RewardLocation(battle.Id, battle.Difficulty, false, reward.ChanceId));
                }
            }
        }

        return locations;

        void Add(string rewardId, RewardLocation location)
        {
            if (string.IsNullOrWhiteSpace(rewardId) || string.Equals(rewardId, "gold", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!locations.TryGetValue(rewardId, out var list))
            {
                list = [];
                locations[rewardId] = list;
            }

            list.Add(location);
        }
    }

    private static List<CatalogFarmLocation> BuildShardLocations(
        string characterId,
        Dictionary<string, List<RewardLocation>> rewardLocations,
        Dictionary<string, CatalogDropChance> dropChanceById)
    {
        var result = new List<CatalogFarmLocation>();
        foreach (var prefix in ShardPrefixes)
        {
            foreach (var location in ResolveLocations(prefix + characterId, rewardLocations, dropChanceById))
            {
                result.Add(location);
            }
        }

        return result;
    }

    private static CatalogFarmLocation[] ResolveLocations(
        string rewardId,
        Dictionary<string, List<RewardLocation>> rewardLocations,
        Dictionary<string, CatalogDropChance> dropChanceById)
    {
        if (!rewardLocations.TryGetValue(rewardId, out var locations))
        {
            return [];
        }

        return locations.Select(location =>
        {
            if (location.Guaranteed || location.ChanceId is null
                || !dropChanceById.TryGetValue(location.ChanceId, out var chance))
            {
                return new CatalogFarmLocation(location.BattleId, location.Difficulty, location.Guaranteed,
                    location.Guaranteed ? null : location.ChanceId, null, null, null);
            }

            return new CatalogFarmLocation(location.BattleId, location.Difficulty, false,
                location.ChanceId, chance.Numerator, chance.Denominator, chance.EffectiveRate);
        }).ToArray();
    }

    private static CatalogEquipmentSlot[] BuildEligibleEquipment(
        CatalogCharacter character,
        string factionId,
        Dictionary<string, CatalogEquipment[]> equipmentByTypeName) =>
        character.EquipmentSlots
            .Select(slot => new CatalogEquipmentSlot(
                slot,
                equipmentByTypeName.TryGetValue(slot, out var items)
                    ? items
                        .Where(item =>
                            item.AllowedFactions.Contains(factionId, StringComparer.Ordinal)
                            || item.AllowedUnits.Contains(character.Id, StringComparer.OrdinalIgnoreCase))
                        .Select(item => item.Id)
                        .ToArray()
                    : []))
            .ToArray();

    private static CatalogUpgradeExpansion ExpandRecipe(
        string upgradeId,
        IReadOnlyDictionary<string, CatalogUpgrade> byId)
    {
        var baseUpgrades = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var craftedUpgrades = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Expand(upgradeId, 1, byId, baseUpgrades, craftedUpgrades, []);

        return new CatalogUpgradeExpansion(
            new ReadOnlyDictionary<string, int>(baseUpgrades),
            new ReadOnlyDictionary<string, int>(craftedUpgrades),
            baseUpgrades.Values.Sum(),
            craftedUpgrades.Values.Sum());
    }

    private static void Expand(
        string upgradeId,
        int multiplier,
        IReadOnlyDictionary<string, CatalogUpgrade> byId,
        IDictionary<string, int> baseUpgrades,
        IDictionary<string, int> craftedUpgrades,
        HashSet<string> stack)
    {
        if (!byId.TryGetValue(upgradeId, out var upgrade) || !upgrade.Craftable || upgrade.Recipe.Count == 0
            || !stack.Add(upgradeId))
        {
            Accumulate(baseUpgrades, upgradeId, multiplier);
            return;
        }

        foreach (var ingredient in upgrade.Recipe)
        {
            if (byId.TryGetValue(ingredient.Material, out var sub) && sub.Craftable && sub.Recipe.Count > 0)
            {
                Accumulate(craftedUpgrades, ingredient.Material, multiplier * ingredient.Count);
            }

            Expand(ingredient.Material, multiplier * ingredient.Count, byId, baseUpgrades, craftedUpgrades, stack);
        }

        stack.Remove(upgradeId);
    }

    private static void Accumulate(IDictionary<string, int> target, string key, int amount) =>
        target[key] = target.TryGetValue(key, out var current) ? current + amount : amount;

    private static CatalogCampaignBattleView BuildBattleView(
        CatalogCampaignBattle battle,
        Dictionary<string, CatalogDropChance> dropChanceById)
    {
        var potential = battle.Rewards.Potential.Select(reward =>
        {
            if (reward.ChanceId is not null && dropChanceById.TryGetValue(reward.ChanceId, out var chance))
            {
                return new CatalogCampaignPotentialRewardView(reward.Id, reward.ChanceId, chance.RewardKind,
                    chance.Numerator, chance.Denominator, chance.EffectiveRate);
            }

            return new CatalogCampaignPotentialRewardView(reward.Id, reward.ChanceId, null, null, null, null);
        }).ToArray();

        return new CatalogCampaignBattleView(
            battle.Id,
            battle.Difficulty,
            battle.EnergyCost,
            battle.NodeNumber,
            battle.Slots,
            new CatalogCampaignRewardsView(battle.Rewards.Guaranteed, potential),
            battle.EnemyPower,
            battle.EnemiesAlliances,
            battle.EnemiesFactions,
            battle.EnemiesTotal,
            battle.EnemiesTypes,
            battle.DetailedEnemyTypes);
    }

    private static CatalogLreTrackView BuildTrackView(
        CatalogLreTrack track,
        IReadOnlyList<(string Id, string Faction, string Alliance)> roster)
    {
        var availableUnitIds = roster
            .Where(unit => track.AllowedUnitsFilter.All(filter => Passes(filter, unit.Faction, unit.Alliance)))
            .Select(unit => unit.Id)
            .ToArray();

        return new CatalogLreTrackView(
            track.Name,
            track.Enemies,
            track.KillPoints,
            track.BattlesPoints,
            track.DefeatAll,
            track.AllowedUnitsFilter,
            track.UnitsRestrictions,
            track.Battles,
            availableUnitIds);
    }

    private static bool Passes(CatalogLreFilter filter, string faction, string alliance)
    {
        var matches = filter.Kind switch
        {
            "Alliance" => string.Equals(alliance, filter.Target, StringComparison.Ordinal),
            "Faction" => string.Equals(faction, filter.Target, StringComparison.Ordinal),
            _ => false,
        };

        return filter.Exclude ? !matches : matches;
    }
}
