using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog;

/// <summary>
/// Loads and validates the embedded game catalog into a <see cref="GameCatalogSnapshot"/>. Source files
/// are discovered by convention — each dataset key maps to the embedded <c>{key}.json</c> (no manifest
/// file) — and release metadata comes from <see cref="GameCatalogRelease"/>. <see cref="Load"/> is public
/// so app startup (and tests) can eagerly load + validate the catalog and fail fast on bad data.
/// </summary>
public static class GameCatalogLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Loads the embedded catalog, denormalizes the served datasets, and validates the result.</summary>
    /// <exception cref="InvalidOperationException">A source file is missing/empty or validation fails.</exception>
    public static GameCatalogSnapshot Load()
    {
        // ---- load raw source collections (by convention: key -> {key}.json) --------------------
        var abilities = LoadDatamine<Dictionary<string, DatamineAbility?>>(GameCatalogDenormalizer.DatamineAbilitiesPath);
        var unitsByFaction = GameCatalogDenormalizer.ReadDatamineUnits(
            LoadDatamine<Dictionary<string, DatamineLineupUnit>>(GameCatalogDenormalizer.DatamineLineupPath), abilities);

        var mowUpgradeCosts = GameCatalogDenormalizer.ReadDatamineMowUpgradeCosts(
            LoadDatamine<List<Dictionary<string, int>>>(GameCatalogDenormalizer.DatamineAbilityUpgradeCostsMoWPath));
        var characterAbilityCosts = GameCatalogDenormalizer.ReadDatamineCharacterAbilityCosts(
            LoadDatamine<List<Dictionary<string, int>>>(GameCatalogDenormalizer.DatamineAbilityUpgradeCostsPath));
        var dropChances = LoadDataset<IReadOnlyList<GameCatalogDropChance>>(GameCatalogDatasets.DropChances);
        var ascensionCosts = GameCatalogDenormalizer.ReadDatamineAscensionCosts(
            LoadDatamine<List<DatamineHeroProgressionStep>>(GameCatalogDenormalizer.DatamineHeroProgressionStepsPath));
        var unlockShardCosts = GameCatalogDenormalizer.ReadDatamineUnlockShardCosts(
            LoadDatamine<Dictionary<string, DatamineHeroConversion>>(GameCatalogDenormalizer.DatamineHeroConversionPath));
        var onslaughtRewards = GameCatalogDenormalizer.ReadDatamineOnslaughtRewards(
            LoadDatamine<DatamineOnslaught>(GameCatalogDenormalizer.DatamineOnslaughtPath));
        var eventDefinitions = LoadDataset<IReadOnlyList<GameCatalogEventDefinition>>(GameCatalogDatasets.EventDefinitions);
        var eventOccurrences = LoadDataset<IReadOnlyList<GameCatalogEventOccurrence>>(GameCatalogDatasets.EventOccurrences);
        var tieredProgressRewards = LoadDatamine<Dictionary<string, List<DatamineTieredProgressReward>>>(
            GameCatalogDenormalizer.DatamineTieredProgressRewardsPath);
        var hseScoring = GameCatalogDenormalizer.ReadDatamineHseScoring(
            LoadDatamine<List<DatamineHomeScreenEvent>>(GameCatalogDenormalizer.DatamineHomeScreenEventsPath),
            tieredProgressRewards,
            abilities);
        var hordeModeTiersRaw = LoadDataset<IReadOnlyList<GameCatalogHordeModeTiers>>(GameCatalogDatasets.HordeModeTiers);
        // groups.json holds chains of several shapes; each reader deserializes only the groups it names.
        var questGroups = LoadDatamine<Dictionary<string, JsonElement>>(GameCatalogDenormalizer.DatamineQuestGroupsPath);
        var mythicQuests = GameCatalogDenormalizer.ReadDatamineMythicQuests(
            questGroups.TryGetValue(GameCatalogDenormalizer.DatamineHeroQuestGroup, out var heroQuests)
                ? heroQuests.Deserialize<DatamineQuestGroup>(JsonOptions)
                    ?? throw new InvalidOperationException($"GameCatalog dataset '{GameCatalogDatasets.MythicQuests}': the hero quest group is empty.")
                : throw new InvalidOperationException($"GameCatalog dataset '{GameCatalogDatasets.MythicQuests}': groups.json has no hero quest group."));
        var unitPower = LoadUnitPower();
        var questEvents = GameCatalogDenormalizer.ReadDatamineQuestEvents(
            LoadDatamine<List<DatamineLinearHeroEvent>>(GameCatalogDenormalizer.DatamineLinearHeroEventsPath));
        var questCrates = LoadDataset<IReadOnlyList<GameCatalogQuestCrate>>(GameCatalogDatasets.QuestCrates);
        var factionViews = GameCatalogDenormalizer.ReadDatamineFactions(
            LoadDatamine<Dictionary<string, DatamineFaction>>(GameCatalogDenormalizer.DatamineFactionsPath));
        var damageTypeViews = GameCatalogDenormalizer.ReadDatamineDamageTypes(
            LoadDatamine<Dictionary<string, DatamineDamageProfile>>(GameCatalogDenormalizer.DatamineDamageProfilesPath));
        var shopProducts = LoadDatamine<Dictionary<string, JsonElement>>(GameCatalogDenormalizer.DatamineShopProductsPath);
        var chests = LoadDatamine<Dictionary<string, JsonElement>>(GameCatalogDenormalizer.DatamineChestsPath);
        var globalValuesViews = GameCatalogDenormalizer.ReadDatamineGlobalValues(
            LoadDatamine<DatamineGlobalValues>(GameCatalogDenormalizer.DatamineGlobalValuesPath),
            LoadDatamine<List<string>>(GameCatalogDenormalizer.DatamineStaminaShopTiersPath),
            shopProducts);
        var progressionRulesViews = GameCatalogDenormalizer.ReadDatamineProgressionRules(
            LoadDatamine<List<int>>(GameCatalogDenormalizer.DatamineXpLevelsPath),
            LoadDatamine<DatamineConsumables>(GameCatalogDenormalizer.DatamineConsumablesPath),
            LoadDatamine<DatamineUpgradeSlots>(GameCatalogDenormalizer.DatamineUpgradeSlotsPath),
            LoadDatamine<List<DatamineHeroProgressionRule>>(GameCatalogDenormalizer.DatamineHeroProgressionStepsPath),
            LoadDatamine<List<DatamineMowProgressionRule>>(GameCatalogDenormalizer.DatamineHeroProgressionStepsMoWPath));
        var hreViews = GameCatalogDenormalizer.ReadDatamineHres(
            new DatamineHreSources(
                LoadDatamine<List<DatamineHeroReleaseEvent>>(GameCatalogDenormalizer.DatamineHeroReleaseEventsPath),
                questGroups,
                LoadDatamine<DatamineLeaderboards>(GameCatalogDenormalizer.DatamineLeaderboardsPath),
                chests,
                shopProducts),
            JsonOptions);

        var datamineNpcs = LoadDatamine<Dictionary<string, DatamineNpc>>(GameCatalogDenormalizer.DatamineNpcPath);
        var npcsByFaction = GameCatalogDenormalizer.ReadDatamineNpcs(datamineNpcs, abilities);

        var equipmentByType = GameCatalogDenormalizer.ReadDatamineEquipment(
            LoadDatamine<Dictionary<string, DatamineItem>>(GameCatalogDenormalizer.DatamineItemsPath));

        var datamineUpgrades = LoadDatamine<Dictionary<string, DatamineUpgrade>>(GameCatalogDenormalizer.DatamineUpgradesPath);
        var upgradesByRarity = GameCatalogDenormalizer.ReadDatamineUpgrades(datamineUpgrades);

        var campaignCuration = LoadDataset<Dictionary<string, GameCatalogCampaignCuration>>(GameCatalogDatasets.CampaignCuration);
        if (!campaignCuration.Keys.Order(StringComparer.Ordinal).SequenceEqual(
                GameCatalogDatasets.CampaignBattleGroups.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"GameCatalog dataset '{GameCatalogDatasets.CampaignCuration}' must curate exactly the CampaignBattleGroups.");
        }

        var campaignGroups = GameCatalogDenormalizer.ReadDatamineCampaigns(
                campaignCuration,
                LoadDatamine<Dictionary<string, List<DatamineCampaign>>>(GameCatalogDenormalizer.DatamineCampaignsPath),
                unitsByFaction,
                datamineNpcs,
                datamineUpgrades,
                LoadDatamine<List<DatamineCampaignEvent>>(GameCatalogDenormalizer.DatamineCampaignEventsPath))
            .ToDictionary(pair => pair.Key, pair => AssignCampaignBattleIndices(pair.Value), StringComparer.Ordinal);

        var lresByEvent = GameCatalogDenormalizer.ReadDatamineLres(
            LoadDataset<Dictionary<string, GameCatalogLreCuration>>(GameCatalogDatasets.LreCuration),
            new DatamineLreSources(
                LoadDatamine<List<DatamineLegendaryHeroEvent>>(GameCatalogDenormalizer.DatamineLegendaryHeroEventsPath),
                LoadDatamine<Dictionary<string, JsonElement>>(GameCatalogDenormalizer.DatamineBattleSetsPath),
                questGroups,
                tieredProgressRewards,
                LoadDatamine<List<DatamineShopSegment>>(GameCatalogDenormalizer.DatamineShopSegmentsPath),
                shopProducts,
                chests,
                LoadDatamine<Dictionary<string, List<DatamineUnitProgressionStep>>>(
                    GameCatalogDenormalizer.DatamineHeroProgressionStepsPerUnitPath)),
            unitsByFaction,
            JsonOptions);

        var rawShopsBySourceKey = GameCatalogDenormalizer.ReadDatamineShops(
            LoadDatamine<Dictionary<string, JsonElement>>(GameCatalogDenormalizer.DatamineMerchantsPath), JsonOptions);

        var raidBossRawData = LoadRaidBossRawData();
        var guildRaidMetaRawData = LoadGuildRaidMetaRawData();

        // ---- build denormalized served datasets ------------------------------------------------
        var characterViews = GameCatalogDenormalizer.BuildCharacters(unitsByFaction, equipmentByType, campaignGroups, dropChances);
        var npcList = GameCatalogDenormalizer.BuildNpcs(npcsByFaction);
        var mowList = GameCatalogDenormalizer.BuildMows(unitsByFaction);
        var mowUpgradeCostViews = GameCatalogDenormalizer.BuildMowUpgradeCosts(mowUpgradeCosts);
        var characterAbilityCostViews = GameCatalogDenormalizer.BuildCharacterAbilityCosts(characterAbilityCosts);
        var ascensionCostViews = GameCatalogDenormalizer.BuildAscensionCosts(ascensionCosts);
        var unlockShardCostViews = GameCatalogDenormalizer.BuildUnlockShardCosts(unlockShardCosts);
        var upgradeViews = GameCatalogDenormalizer.BuildUpgrades(upgradesByRarity, campaignGroups, dropChances);
        var equipmentViews = GameCatalogDenormalizer.BuildEquipment(equipmentByType);
        var campaignBattleViews = GameCatalogDenormalizer.BuildCampaignBattles(campaignGroups, dropChances);
        var campaignDefinitionViews = GameCatalogDenormalizer.BuildCampaignDefinitions(campaignGroups);
        var lreViews = GameCatalogDenormalizer.BuildLres(lresByEvent, unitsByFaction);
        var lreBattleViews = GameCatalogDenormalizer.BuildLreBattles(lresByEvent);
        var hordeModeTiersViews = GameCatalogDenormalizer.BuildHordeModeTiers(hordeModeTiersRaw);
        var eventDefinitionViews = GameCatalogDenormalizer.BuildEventDefinitions(eventDefinitions, hseScoring);
        var loadTime = DateTimeOffset.UtcNow;
        var eventsCalendar = GameCatalogDenormalizer.BuildEventsCalendar(eventDefinitions, eventOccurrences, loadTime);
        var shopViews = GameCatalogDenormalizer.BuildShops(rawShopsBySourceKey);
        var raidBossesView = GameCatalogDenormalizer.BuildRaidBosses(raidBossRawData);
        var guildRaidMetaView = GameCatalogDenormalizer.BuildGuildRaidMeta(guildRaidMetaRawData);
        var mythicQuestViews = GameCatalogDenormalizer.BuildMythicQuests(mythicQuests);
        var unitPowerViews = GameCatalogDenormalizer.BuildUnitPower(
            unitPower,
            unitsByFaction.Values.SelectMany(faction => faction.Characters).ToArray(),
            unitsByFaction.Values.SelectMany(faction => faction.Mows).ToArray());
        var questEventViews = GameCatalogDenormalizer.BuildQuestEvents(questEvents);
        var questCrateViews = GameCatalogDenormalizer.BuildQuestCrates(questCrates);

        // Served dataset hashes are computed over the canonical JSON of each denormalized payload; the payload
        // types are recorded on the way for the schema hash.
        var payloadTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
        string HashServed<T>(string key, T payload)
        {
            payloadTypes[key] = typeof(T);
            return GameCatalogHashing.ComputeCanonicalJsonHash(payload, JsonOptions);
        }

        var datasetHashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [GameCatalogDatasets.Characters] = HashServed(GameCatalogDatasets.Characters, characterViews),
            [GameCatalogDatasets.Npcs] = HashServed(GameCatalogDatasets.Npcs, npcList),
            [GameCatalogDatasets.Mows] = HashServed(GameCatalogDatasets.Mows, mowList),
            [GameCatalogDatasets.MowUpgradeCostsServed] = HashServed(GameCatalogDatasets.MowUpgradeCostsServed, mowUpgradeCostViews),
            [GameCatalogDatasets.CharacterAbilityCostsServed] = HashServed(GameCatalogDatasets.CharacterAbilityCostsServed, characterAbilityCostViews),
            [GameCatalogDatasets.AscensionCostsServed] = HashServed(GameCatalogDatasets.AscensionCostsServed, ascensionCostViews),
            [GameCatalogDatasets.UnlockShardCostsServed] = HashServed(GameCatalogDatasets.UnlockShardCostsServed, unlockShardCostViews),
            [GameCatalogDatasets.OnslaughtRewards] = HashServed(GameCatalogDatasets.OnslaughtRewards, onslaughtRewards),
            [GameCatalogDatasets.Upgrades] = HashServed(GameCatalogDatasets.Upgrades, upgradeViews),
            [GameCatalogDatasets.Equipment] = HashServed(GameCatalogDatasets.Equipment, equipmentViews),
            [GameCatalogDatasets.CampaignBattles] = HashServed(GameCatalogDatasets.CampaignBattles, campaignBattleViews),
            [GameCatalogDatasets.CampaignDefinitions] = HashServed(GameCatalogDatasets.CampaignDefinitions, campaignDefinitionViews),
            [GameCatalogDatasets.Lres] = HashServed(GameCatalogDatasets.Lres, lreViews),
            [GameCatalogDatasets.LreBattles] = HashServed(GameCatalogDatasets.LreBattles, lreBattleViews),
            [GameCatalogDatasets.HordeModeTiers] = HashServed(GameCatalogDatasets.HordeModeTiers, hordeModeTiersViews),
            [GameCatalogDatasets.EventDefinitionsServed] = HashServed(GameCatalogDatasets.EventDefinitionsServed, eventDefinitionViews),
            [GameCatalogDatasets.EventsCalendar] = HashServed(GameCatalogDatasets.EventsCalendar, eventsCalendar),
            [GameCatalogDatasets.Shops] = HashServed(GameCatalogDatasets.Shops, shopViews),
            [GameCatalogDatasets.RaidBosses] = HashServed(GameCatalogDatasets.RaidBosses, raidBossesView),
            [GameCatalogDatasets.GuildRaidMeta] = HashServed(GameCatalogDatasets.GuildRaidMeta, guildRaidMetaView),
            [GameCatalogDatasets.MythicQuestsServed] = HashServed(GameCatalogDatasets.MythicQuestsServed, mythicQuestViews),
            [GameCatalogDatasets.UnitPower] = HashServed(GameCatalogDatasets.UnitPower, unitPowerViews),
            [GameCatalogDatasets.QuestEvents] = HashServed(GameCatalogDatasets.QuestEvents, questEventViews),
            [GameCatalogDatasets.QuestCrates] = HashServed(GameCatalogDatasets.QuestCrates, questCrateViews),
            [GameCatalogDatasets.Factions] = HashServed(GameCatalogDatasets.Factions, factionViews),
            [GameCatalogDatasets.DamageTypes] = HashServed(GameCatalogDatasets.DamageTypes, damageTypeViews),
            [GameCatalogDatasets.GlobalValues] = HashServed(GameCatalogDatasets.GlobalValues, globalValuesViews),
            [GameCatalogDatasets.ProgressionRules] = HashServed(GameCatalogDatasets.ProgressionRules, progressionRulesViews),
            [GameCatalogDatasets.Hre] = HashServed(GameCatalogDatasets.Hre, hreViews),
        };

        var schemaHash = GameCatalogContractHashing.ComputeContractHash(payloadTypes, JsonOptions);
        var gameVersion = GameCatalogRelease.GameVersion;
        var snapshot = new GameCatalogSnapshot(
            GameCatalogRelease.Version,
            schemaHash,
            gameVersion,
            GameCatalogHashing.ComputeSnapshotHash(
                GameCatalogRelease.Version, schemaHash, gameVersion, datasetHashes),
            new ReadOnlyDictionary<string, string>(datasetHashes),
            unitsByFaction,
            new ReadOnlyCollection<GameCatalogMowUpgradeCost>(mowUpgradeCosts.ToArray()),
            new ReadOnlyCollection<GameCatalogCharacterAbilityCost>(characterAbilityCosts.ToArray()),
            new ReadOnlyCollection<GameCatalogAscensionCost>(ascensionCosts.ToArray()),
            new ReadOnlyCollection<GameCatalogUnlockShardCost>(unlockShardCosts.ToArray()),
            new ReadOnlyCollection<GameCatalogOnslaughtReward>(onslaughtRewards.ToArray()),
            eventDefinitions,
            eventOccurrences,
            new ReadOnlyCollection<GameCatalogHseScoring>(hseScoring.ToArray()),
            npcsByFaction,
            equipmentByType,
            upgradesByRarity,
            new ReadOnlyDictionary<string, GameCatalogCampaignGroup>(campaignGroups),
            new ReadOnlyCollection<GameCatalogDropChance>(dropChances.ToArray()),
            new ReadOnlyDictionary<string, GameCatalogLre>(lresByEvent),
            rawShopsBySourceKey,
            raidBossRawData,
            guildRaidMetaRawData,
            new ReadOnlyCollection<GameCatalogRawMythicQuests>(mythicQuests.ToArray()),
            unitPower,
            new ReadOnlyCollection<GameCatalogRawQuestEvent>(questEvents.ToArray()),
            new ReadOnlyCollection<GameCatalogQuestCrate>(questCrates.ToArray()),
            characterViews,
            npcList,
            mowList,
            mowUpgradeCostViews,
            characterAbilityCostViews,
            ascensionCostViews,
            unlockShardCostViews,
            upgradeViews,
            equipmentViews,
            campaignBattleViews,
            campaignDefinitionViews,
            lreViews,
            lreBattleViews,
            hordeModeTiersViews,
            eventDefinitionViews,
            eventsCalendar,
            shopViews,
            raidBossesView,
            guildRaidMetaView,
            mythicQuestViews,
            unitPowerViews,
            questEventViews,
            questCrateViews,
            factionViews,
            damageTypeViews,
            globalValuesViews,
            progressionRulesViews,
            hreViews);

        var errors = GameCatalogValidator.Validate(snapshot);
        if (errors.Count > 0)
        {
            var details = string.Join("; ", errors.Select(error => $"[{error.Dataset}/{error.Code}] {error.Message}"));
            throw new InvalidOperationException($"Game catalog validation failed: {details}");
        }

        return snapshot;
    }

    /// <summary>
    /// Assembles the raid-boss raw data from the datamine guildBoss files: the season-config rotation, the primarch
    /// prime ids, the modifier definitions, the unit sets (merged across sets) and the season configs.
    /// </summary>
    private static GameCatalogRaidBossRawData LoadRaidBossRawData() =>
        new(
            LoadDatamine<List<string>>(GameCatalogDenormalizer.DatamineGuildBossRotationPath),
            LoadDatamine<List<string>>(GameCatalogDenormalizer.DatamineGuildBossPrimarchsPath),
            GameCatalogDenormalizer.ReadDatamineRaidBossUnitSets(
                LoadDatamine<Dictionary<string, Dictionary<string, GameCatalogRaidBossRawUnitSet>>>(
                    GameCatalogDenormalizer.DatamineGuildBossUnitSetsPath)),
            LoadDatamine<Dictionary<string, GameCatalogRaidBossRawSeason>>(GameCatalogDenormalizer.DatamineGuildBossSeasonsPath),
            LoadDatamine<Dictionary<string, GameCatalogRaidBossRawModifier>>(GameCatalogDenormalizer.DatamineGuildBossModifiersPath));

    /// <summary>Assembles the unit-power tables from their datamine files.</summary>
    private static GameCatalogRawUnitPower LoadUnitPower() =>
        new(
            LoadDatamine<List<GameCatalogRawHeroPowerStep>>(GameCatalogDenormalizer.DatamineHeroProgressionStepsPath),
            LoadDatamine<List<GameCatalogRawMowPowerStep>>(GameCatalogDenormalizer.DatamineHeroProgressionStepsMoWPath),
            LoadDatamine<Dictionary<string, int>>(GameCatalogDenormalizer.DatamineDamageProfileModifiersPath),
            LoadDatamine<Dictionary<string, int>>(GameCatalogDenormalizer.DatamineTraitPowerModifiersPath),
            LoadDatamine<Dictionary<string, GameCatalogRawAbilityPowerModifier>>(GameCatalogDenormalizer.DatamineAbilityPowerModifiersPath),
            LoadDatamine<GameCatalogAbilityPowerCurves>(GameCatalogDenormalizer.DatamineAbilityPowerCurvePath),
            LoadDatamine<Dictionary<string, GameCatalogRawUnitPowerEntry>>(GameCatalogDenormalizer.DatamineLineupPath));

    /// <summary>
    /// Assembles the raw Guild Raid Meta data from <c>guild-raid-comps.json</c> (Comp guidance) plus one
    /// <c>guild-raid-meta-boss-{n}-{Slug}</c> file per boss (that boss's own recommendations, and any
    /// primes fought alongside it). A duplicate boss unit-set id or prime unit-set id across files throws.
    /// </summary>
    private static GameCatalogGuildRaidMetaRawData LoadGuildRaidMetaRawData()
    {
        var comps = LoadDataset<GameCatalogGuildRaidMetaRawCompsFile>(GameCatalogDatasets.GuildRaidMetaComps);

        var bosses = new List<GameCatalogGuildRaidMetaRawBoss>();
        var primes = new Dictionary<string, GameCatalogGuildRaidMetaRawPrime>(StringComparer.Ordinal);

        foreach (var key in GameCatalogDatasets.GuildRaidMetaBossGroups)
        {
            var file = LoadDataset<GameCatalogGuildRaidMetaRawBossFile>(key);

            bosses.Add(new GameCatalogGuildRaidMetaRawBoss(file.BossUnitSetId, file.PrimeUnitSetIds, file.Recommendations));

            foreach (var prime in file.Primes ?? [])
            {
                if (!primes.TryAdd(prime.PrimeUnitSetId, prime))
                {
                    throw new InvalidOperationException(
                        $"Guild Raid Meta prime unit-set id '{prime.PrimeUnitSetId}' is defined in more than one guild-raid-meta-boss-*.json file.");
                }
            }
        }

        if (bosses.Select(boss => boss.BossUnitSetId).Distinct(StringComparer.Ordinal).Count() != bosses.Count)
        {
            throw new InvalidOperationException(
                "Guild Raid Meta boss unit-set id is defined in more than one guild-raid-meta-boss-*.json file.");
        }

        return new GameCatalogGuildRaidMetaRawData(comps.SourceId, comps.UpdatedOn, comps.Comps, bosses, primes.Values.ToArray());
    }

    private static T LoadDataset<T>(string key)
    {
        // Straight from the resource stream: no intermediate JsonDocument.
        using var stream = OpenEmbeddedJson($"{key}.json");

        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"GameCatalog dataset '{key}' is empty.");
    }

    private static T LoadDatamine<T>(string rawPath)
    {
        using var stream = DatamineFiles.Open(rawPath);

        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Datamine file 'raw/{rawPath}' is empty.");
    }

    private static GameCatalogCampaignGroup AssignCampaignBattleIndices(GameCatalogCampaignGroup group)
    {
        var nextIndexByType = new Dictionary<string, int>(StringComparer.Ordinal);
        var battles = group.Battles.Select(battle =>
        {
            var index = nextIndexByType.GetValueOrDefault(battle.Type);
            nextIndexByType[battle.Type] = index + 1;
            return battle with { BattleIndex = index };
        }).ToArray();

        return group with { Battles = battles };
    }

    private static Stream OpenEmbeddedJson(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // MSBuild rewrites hyphenated folder names in the resource name (campaign-battles ->
        // campaign_battles) but keeps file names verbatim. Leaf file names are unique across the
        // catalog and are always preceded by a '.' segment separator, so match on the leaf.
        // Datamine copies are never matched here (see DatamineFiles).
        var leafName = fileName.Replace('\\', '/').Split('/')[^1];
        var resourceSuffix = $".{leafName}";
        var resourceName = assembly
            .GetManifestResourceNames()
            .Where(name => !name.StartsWith(DatamineFiles.ResourcePrefix, StringComparison.Ordinal))
            .SingleOrDefault(name => name.EndsWith(resourceSuffix, StringComparison.Ordinal));

        if (resourceName is null)
        {
            throw new InvalidOperationException($"Embedded game catalog data file '{fileName}' was not found.");
        }

        return assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded game catalog data file '{fileName}' could not be opened.");
    }
}
