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
        var onslaughtRewards = LoadDataset<IReadOnlyList<GameCatalogOnslaughtReward>>(GameCatalogDatasets.OnslaughtRewards);
        var eventDefinitions = LoadDataset<IReadOnlyList<GameCatalogEventDefinition>>(GameCatalogDatasets.EventDefinitions);
        var eventOccurrences = LoadDataset<IReadOnlyList<GameCatalogEventOccurrence>>(GameCatalogDatasets.EventOccurrences);
        var hseScoring = LoadDataset<IReadOnlyList<GameCatalogHseScoring>>(GameCatalogDatasets.HomeScreenEventScoring);
        var hordeModeTiersRaw = LoadDataset<IReadOnlyList<GameCatalogHordeModeTiers>>(GameCatalogDatasets.HordeModeTiers);
        var mythicQuests = GameCatalogDenormalizer.ReadDatamineMythicQuests(
            LoadDatamine<DatamineQuestGroups>(GameCatalogDenormalizer.DatamineQuestGroupsPath).Hero);
        var unitPower = LoadUnitPower();
        var questEvents = LoadDataset<IReadOnlyList<GameCatalogRawQuestEvent>>(GameCatalogDatasets.QuestEvents);
        var questCrates = LoadDataset<IReadOnlyList<GameCatalogQuestCrate>>(GameCatalogDatasets.QuestCrates);

        var npcsByFaction = GameCatalogDenormalizer.ReadDatamineNpcs(
            LoadDatamine<Dictionary<string, DatamineNpc>>(GameCatalogDenormalizer.DatamineNpcPath), abilities);

        var equipmentByType = GameCatalogDenormalizer.ReadDatamineEquipment(
            LoadDatamine<Dictionary<string, DatamineItem>>(GameCatalogDenormalizer.DatamineItemsPath));

        var upgradesByRarity = GameCatalogDenormalizer.ReadDatamineUpgrades(
            LoadDatamine<Dictionary<string, DatamineUpgrade>>(GameCatalogDenormalizer.DatamineUpgradesPath));

        var campaignGroups = new Dictionary<string, GameCatalogCampaignGroup>(StringComparer.Ordinal);
        foreach (var key in GameCatalogDatasets.CampaignBattleGroups)
        {
            campaignGroups[key] = AssignCampaignBattleIndices(LoadDataset<GameCatalogCampaignGroup>(key));
        }

        var lresByEvent = new Dictionary<string, GameCatalogLre>(StringComparer.Ordinal);
        foreach (var key in GameCatalogDatasets.LreEvents)
        {
            lresByEvent[key] = LoadDataset<GameCatalogLre>(key);
        }

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

        // Served dataset hashes are computed over the canonical JSON of each denormalized payload.
        var datasetHashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [GameCatalogDatasets.Characters] = GameCatalogHashing.ComputeCanonicalJsonHash(characterViews, JsonOptions),
            [GameCatalogDatasets.Npcs] = GameCatalogHashing.ComputeCanonicalJsonHash(npcList, JsonOptions),
            [GameCatalogDatasets.Mows] = GameCatalogHashing.ComputeCanonicalJsonHash(mowList, JsonOptions),
            [GameCatalogDatasets.MowUpgradeCostsServed] = GameCatalogHashing.ComputeCanonicalJsonHash(mowUpgradeCostViews, JsonOptions),
            [GameCatalogDatasets.CharacterAbilityCostsServed] = GameCatalogHashing.ComputeCanonicalJsonHash(characterAbilityCostViews, JsonOptions),
            [GameCatalogDatasets.AscensionCostsServed] = GameCatalogHashing.ComputeCanonicalJsonHash(ascensionCostViews, JsonOptions),
            [GameCatalogDatasets.UnlockShardCostsServed] = GameCatalogHashing.ComputeCanonicalJsonHash(unlockShardCostViews, JsonOptions),
            [GameCatalogDatasets.OnslaughtRewards] = GameCatalogHashing.ComputeCanonicalJsonHash(onslaughtRewards, JsonOptions),
            [GameCatalogDatasets.Upgrades] = GameCatalogHashing.ComputeCanonicalJsonHash(upgradeViews, JsonOptions),
            [GameCatalogDatasets.Equipment] = GameCatalogHashing.ComputeCanonicalJsonHash(equipmentViews, JsonOptions),
            [GameCatalogDatasets.CampaignBattles] = GameCatalogHashing.ComputeCanonicalJsonHash(campaignBattleViews, JsonOptions),
            [GameCatalogDatasets.CampaignDefinitions] = GameCatalogHashing.ComputeCanonicalJsonHash(campaignDefinitionViews, JsonOptions),
            [GameCatalogDatasets.Lres] = GameCatalogHashing.ComputeCanonicalJsonHash(lreViews, JsonOptions),
            [GameCatalogDatasets.LreBattles] = GameCatalogHashing.ComputeCanonicalJsonHash(lreBattleViews, JsonOptions),
            [GameCatalogDatasets.HordeModeTiers] = GameCatalogHashing.ComputeCanonicalJsonHash(hordeModeTiersViews, JsonOptions),
            [GameCatalogDatasets.EventDefinitionsServed] = GameCatalogHashing.ComputeCanonicalJsonHash(eventDefinitionViews, JsonOptions),
            [GameCatalogDatasets.EventsCalendar] = GameCatalogHashing.ComputeCanonicalJsonHash(eventsCalendar, JsonOptions),
            [GameCatalogDatasets.Shops] = GameCatalogHashing.ComputeCanonicalJsonHash(shopViews, JsonOptions),
            [GameCatalogDatasets.RaidBosses] = GameCatalogHashing.ComputeCanonicalJsonHash(raidBossesView, JsonOptions),
            [GameCatalogDatasets.GuildRaidMeta] = GameCatalogHashing.ComputeCanonicalJsonHash(guildRaidMetaView, JsonOptions),
            [GameCatalogDatasets.MythicQuestsServed] = GameCatalogHashing.ComputeCanonicalJsonHash(mythicQuestViews, JsonOptions),
            [GameCatalogDatasets.UnitPower] = GameCatalogHashing.ComputeCanonicalJsonHash(unitPowerViews, JsonOptions),
            [GameCatalogDatasets.QuestEvents] = GameCatalogHashing.ComputeCanonicalJsonHash(questEventViews, JsonOptions),
            [GameCatalogDatasets.QuestCrates] = GameCatalogHashing.ComputeCanonicalJsonHash(questCrateViews, JsonOptions),
        };

        var snapshot = new GameCatalogSnapshot(
            GameCatalogRelease.Version,
            GameCatalogRelease.SchemaVersion,
            GameCatalogRelease.GameVersion,
            GameCatalogHashing.ComputeSnapshotHash(
                GameCatalogRelease.Version, GameCatalogRelease.SchemaVersion, GameCatalogRelease.GameVersion, datasetHashes),
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
            questCrateViews);

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
