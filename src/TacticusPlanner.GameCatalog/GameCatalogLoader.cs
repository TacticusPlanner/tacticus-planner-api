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
        var unitsByFaction = new Dictionary<string, GameCatalogFactionUnits>(StringComparer.Ordinal);
        foreach (var key in GameCatalogDatasets.UnitFactions)
        {
            unitsByFaction[key] = LoadDataset<GameCatalogFactionUnits>(key);
        }

        var mowUpgradeCosts = LoadDataset<IReadOnlyList<GameCatalogMowUpgradeCost>>(GameCatalogDatasets.MowUpgradeCosts);
        var equipmentUpgradeCosts = LoadDataset<IReadOnlyList<GameCatalogEquipmentUpgradeCost>>(GameCatalogDatasets.EquipmentUpgradeCosts);
        var dropChances = LoadDataset<IReadOnlyList<GameCatalogDropChance>>(GameCatalogDatasets.DropChances);
        var ascensionCosts = LoadDataset<IReadOnlyList<GameCatalogAscensionCost>>(GameCatalogDatasets.AscensionCosts);
        var unlockShardCosts = LoadDataset<IReadOnlyList<GameCatalogUnlockShardCost>>(GameCatalogDatasets.UnlockShardCosts);
        var onslaughtRewards = LoadDataset<IReadOnlyList<GameCatalogOnslaughtReward>>(GameCatalogDatasets.OnslaughtRewards);

        var npcsByFaction = new Dictionary<string, GameCatalogFactionNpcs>(StringComparer.Ordinal);
        foreach (var key in GameCatalogDatasets.NpcFactions)
        {
            npcsByFaction[key] = LoadDataset<GameCatalogFactionNpcs>(key);
        }

        var equipmentByType = new Dictionary<string, IReadOnlyList<GameCatalogEquipment>>(StringComparer.Ordinal);
        foreach (var key in GameCatalogDatasets.EquipmentTypes)
        {
            equipmentByType[key] = LoadDataset<IReadOnlyList<GameCatalogEquipment>>(key);
        }

        var upgradesByRarity = new Dictionary<string, IReadOnlyList<GameCatalogUpgrade>>(StringComparer.Ordinal);
        foreach (var key in GameCatalogDatasets.UpgradeRarities)
        {
            upgradesByRarity[key] = LoadDataset<IReadOnlyList<GameCatalogUpgrade>>(key);
        }

        var campaignGroups = new Dictionary<string, GameCatalogCampaignGroup>(StringComparer.Ordinal);
        foreach (var key in GameCatalogDatasets.CampaignBattleGroups)
        {
            campaignGroups[key] = LoadDataset<GameCatalogCampaignGroup>(key);
        }

        var lresByEvent = new Dictionary<string, GameCatalogLre>(StringComparer.Ordinal);
        foreach (var key in GameCatalogDatasets.LreEvents)
        {
            lresByEvent[key] = LoadDataset<GameCatalogLre>(key);
        }

        // ---- build denormalized served datasets ------------------------------------------------
        var characterViews = GameCatalogDenormalizer.BuildCharacters(unitsByFaction, equipmentByType, campaignGroups, dropChances);
        var npcList = GameCatalogDenormalizer.BuildNpcs(npcsByFaction);
        var mowList = GameCatalogDenormalizer.BuildMows(unitsByFaction);
        var mowUpgradeCostViews = GameCatalogDenormalizer.BuildMowUpgradeCosts(mowUpgradeCosts);
        var ascensionCostViews = GameCatalogDenormalizer.BuildAscensionCosts(ascensionCosts);
        var unlockShardCostViews = GameCatalogDenormalizer.BuildUnlockShardCosts(unlockShardCosts);
        var upgradeViews = GameCatalogDenormalizer.BuildUpgrades(upgradesByRarity, campaignGroups, dropChances);
        var equipmentViews = GameCatalogDenormalizer.BuildEquipment(equipmentByType, equipmentUpgradeCosts);
        var campaignBattleViews = GameCatalogDenormalizer.BuildCampaignBattles(campaignGroups, dropChances);
        var campaignDefinitionViews = GameCatalogDenormalizer.BuildCampaignDefinitions(campaignGroups);
        var lreViews = GameCatalogDenormalizer.BuildLres(lresByEvent, unitsByFaction);
        var lreBattleViews = GameCatalogDenormalizer.BuildLreBattles(lresByEvent);
        var lreCommonViews = GameCatalogDenormalizer.BuildLreCommon(lresByEvent);

        // Served dataset hashes are computed over the canonical JSON of each denormalized payload.
        var datasetHashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [GameCatalogDatasets.Characters] = GameCatalogHashing.ComputeCanonicalJsonHash(characterViews, JsonOptions),
            [GameCatalogDatasets.Npcs] = GameCatalogHashing.ComputeCanonicalJsonHash(npcList, JsonOptions),
            [GameCatalogDatasets.Mows] = GameCatalogHashing.ComputeCanonicalJsonHash(mowList, JsonOptions),
            [GameCatalogDatasets.MowUpgradeCostsServed] = GameCatalogHashing.ComputeCanonicalJsonHash(mowUpgradeCostViews, JsonOptions),
            [GameCatalogDatasets.AscensionCostsServed] = GameCatalogHashing.ComputeCanonicalJsonHash(ascensionCostViews, JsonOptions),
            [GameCatalogDatasets.UnlockShardCostsServed] = GameCatalogHashing.ComputeCanonicalJsonHash(unlockShardCostViews, JsonOptions),
            [GameCatalogDatasets.OnslaughtRewards] = GameCatalogHashing.ComputeCanonicalJsonHash(onslaughtRewards, JsonOptions),
            [GameCatalogDatasets.Upgrades] = GameCatalogHashing.ComputeCanonicalJsonHash(upgradeViews, JsonOptions),
            [GameCatalogDatasets.Equipment] = GameCatalogHashing.ComputeCanonicalJsonHash(equipmentViews, JsonOptions),
            [GameCatalogDatasets.CampaignBattles] = GameCatalogHashing.ComputeCanonicalJsonHash(campaignBattleViews, JsonOptions),
            [GameCatalogDatasets.CampaignDefinitions] = GameCatalogHashing.ComputeCanonicalJsonHash(campaignDefinitionViews, JsonOptions),
            [GameCatalogDatasets.Lres] = GameCatalogHashing.ComputeCanonicalJsonHash(lreViews, JsonOptions),
            [GameCatalogDatasets.LreBattles] = GameCatalogHashing.ComputeCanonicalJsonHash(lreBattleViews, JsonOptions),
            [GameCatalogDatasets.LreCommon] = GameCatalogHashing.ComputeCanonicalJsonHash(lreCommonViews, JsonOptions),
        };

        var snapshot = new GameCatalogSnapshot(
            GameCatalogRelease.Version,
            GameCatalogRelease.SchemaVersion,
            GameCatalogRelease.GameVersion,
            GameCatalogHashing.ComputeSnapshotHash(
                GameCatalogRelease.Version, GameCatalogRelease.SchemaVersion, GameCatalogRelease.GameVersion, datasetHashes),
            new ReadOnlyDictionary<string, string>(datasetHashes),
            new ReadOnlyDictionary<string, GameCatalogFactionUnits>(unitsByFaction),
            new ReadOnlyCollection<GameCatalogMowUpgradeCost>(mowUpgradeCosts.ToArray()),
            new ReadOnlyCollection<GameCatalogEquipmentUpgradeCost>(equipmentUpgradeCosts.ToArray()),
            new ReadOnlyCollection<GameCatalogAscensionCost>(ascensionCosts.ToArray()),
            new ReadOnlyCollection<GameCatalogUnlockShardCost>(unlockShardCosts.ToArray()),
            new ReadOnlyCollection<GameCatalogOnslaughtReward>(onslaughtRewards.ToArray()),
            new ReadOnlyDictionary<string, GameCatalogFactionNpcs>(npcsByFaction),
            new ReadOnlyDictionary<string, IReadOnlyList<GameCatalogEquipment>>(equipmentByType),
            new ReadOnlyDictionary<string, IReadOnlyList<GameCatalogUpgrade>>(upgradesByRarity),
            new ReadOnlyDictionary<string, GameCatalogCampaignGroup>(campaignGroups),
            new ReadOnlyCollection<GameCatalogDropChance>(dropChances.ToArray()),
            new ReadOnlyDictionary<string, GameCatalogLre>(lresByEvent),
            characterViews,
            npcList,
            mowList,
            mowUpgradeCostViews,
            ascensionCostViews,
            unlockShardCostViews,
            upgradeViews,
            equipmentViews,
            campaignBattleViews,
            campaignDefinitionViews,
            lreViews,
            lreBattleViews,
            lreCommonViews);

        var errors = GameCatalogValidator.Validate(snapshot);
        if (errors.Count > 0)
        {
            var details = string.Join("; ", errors.Select(error => $"[{error.Dataset}/{error.Code}] {error.Message}"));
            throw new InvalidOperationException($"Game catalog validation failed: {details}");
        }

        return snapshot;
    }

    private static T LoadDataset<T>(string key)
    {
        using var document = ReadEmbeddedJsonDocument($"{key}.json");

        return document.RootElement.Deserialize<T>(JsonOptions)
            ?? throw new InvalidOperationException($"GameCatalog dataset '{key}' is empty.");
    }

    private static JsonDocument ReadEmbeddedJsonDocument(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // MSBuild rewrites hyphenated folder names in the resource name (campaign-battles ->
        // campaign_battles) but keeps file names verbatim. Leaf file names are unique across the
        // catalog and are always preceded by a '.' segment separator, so match on the leaf.
        var leafName = fileName.Replace('\\', '/').Split('/')[^1];
        var resourceSuffix = $".{leafName}";
        var resourceName = assembly
            .GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(resourceSuffix, StringComparison.Ordinal));

        if (resourceName is null)
        {
            throw new InvalidOperationException($"Embedded game catalog data file '{fileName}' was not found.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded game catalog data file '{fileName}' could not be opened.");

        return JsonDocument.Parse(stream);
    }
}
