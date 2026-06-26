using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;

namespace TacticusPlanner.GameCatalog;

internal sealed class EmbeddedGameCatalogProvider : IGameCatalogProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public EmbeddedGameCatalogProvider()
    {
        Current = LoadSnapshot();
    }

    public GameCatalogSnapshot Current { get; }

    private static GameCatalogSnapshot LoadSnapshot()
    {
        using var manifestDocument = ReadEmbeddedJsonDocument("catalog-manifest.json");
        var manifest = manifestDocument.RootElement.Deserialize<GameCatalogManifestSource>(JsonOptions)
            ?? throw new InvalidOperationException("GameCatalog manifest is empty.");

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new InvalidOperationException("GameCatalog manifest version is required.");
        }

        if (manifest.SchemaVersion < 1)
        {
            throw new InvalidOperationException("GameCatalog manifest schema version must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(manifest.GameVersion))
        {
            throw new InvalidOperationException("GameCatalog manifest game version is required.");
        }

        var datasets = manifest.Datasets.ToDictionary(dataset => dataset.Key, StringComparer.Ordinal);

        // ---- load raw source collections -------------------------------------------------------
        var unitsByFaction = new Dictionary<string, GameCatalogFactionUnits>(StringComparer.Ordinal);
        foreach (var key in GameCatalogDatasets.UnitFactions)
        {
            unitsByFaction[key] = LoadDataset<GameCatalogFactionUnits>(datasets, key);
        }

        var mowUpgradeCosts = LoadDataset<IReadOnlyList<GameCatalogMowUpgradeCost>>(datasets, GameCatalogDatasets.MowUpgradeCosts);
        var equipmentUpgradeCosts = LoadDataset<IReadOnlyList<GameCatalogEquipmentUpgradeCost>>(datasets, GameCatalogDatasets.EquipmentUpgradeCosts);
        var dropChances = LoadDataset<IReadOnlyList<GameCatalogDropChance>>(datasets, GameCatalogDatasets.DropChances);

        var npcsByFaction = new Dictionary<string, GameCatalogFactionNpcs>(StringComparer.Ordinal);
        foreach (var key in GameCatalogDatasets.NpcFactions)
        {
            npcsByFaction[key] = LoadDataset<GameCatalogFactionNpcs>(datasets, key);
        }

        var equipmentByType = new Dictionary<string, IReadOnlyList<GameCatalogEquipment>>(StringComparer.Ordinal);
        foreach (var key in GameCatalogDatasets.EquipmentTypes)
        {
            equipmentByType[key] = LoadDataset<IReadOnlyList<GameCatalogEquipment>>(datasets, key);
        }

        var upgradesByRarity = new Dictionary<string, IReadOnlyList<GameCatalogUpgrade>>(StringComparer.Ordinal);
        foreach (var key in GameCatalogDatasets.UpgradeRarities)
        {
            upgradesByRarity[key] = LoadDataset<IReadOnlyList<GameCatalogUpgrade>>(datasets, key);
        }

        var campaignGroups = new Dictionary<string, GameCatalogCampaignGroup>(StringComparer.Ordinal);
        foreach (var key in GameCatalogDatasets.CampaignBattleGroups)
        {
            campaignGroups[key] = LoadDataset<GameCatalogCampaignGroup>(datasets, key);
        }

        var lresByEvent = new Dictionary<string, GameCatalogLre>(StringComparer.Ordinal);
        foreach (var key in GameCatalogDatasets.LreEvents)
        {
            lresByEvent[key] = LoadDataset<GameCatalogLre>(datasets, key);
        }

        // ---- build denormalized served datasets ------------------------------------------------
        var characterViews = GameCatalogDenormalizer.BuildCharacters(unitsByFaction, equipmentByType, campaignGroups, dropChances);
        var npcList = GameCatalogDenormalizer.BuildNpcs(npcsByFaction);
        var mowList = GameCatalogDenormalizer.BuildMows(unitsByFaction);
        var upgradeViews = GameCatalogDenormalizer.BuildUpgrades(upgradesByRarity, campaignGroups, dropChances);
        var equipmentViews = GameCatalogDenormalizer.BuildEquipment(equipmentByType, equipmentUpgradeCosts);
        var campaignBattleViews = GameCatalogDenormalizer.BuildCampaignBattles(campaignGroups, dropChances);
        var campaignDefinitionViews = GameCatalogDenormalizer.BuildCampaignDefinitions(campaignGroups);
        var lreViews = GameCatalogDenormalizer.BuildLres(lresByEvent, unitsByFaction);

        // Served dataset hashes are computed over the canonical JSON of each denormalized payload.
        var datasetHashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [GameCatalogDatasets.Characters] = GameCatalogHashing.ComputeCanonicalJsonHash(characterViews, JsonOptions),
            [GameCatalogDatasets.Npcs] = GameCatalogHashing.ComputeCanonicalJsonHash(npcList, JsonOptions),
            [GameCatalogDatasets.Mows] = GameCatalogHashing.ComputeCanonicalJsonHash(mowList, JsonOptions),
            [GameCatalogDatasets.MowUpgradeCostsServed] = GameCatalogHashing.ComputeCanonicalJsonHash(mowUpgradeCosts, JsonOptions),
            [GameCatalogDatasets.Upgrades] = GameCatalogHashing.ComputeCanonicalJsonHash(upgradeViews, JsonOptions),
            [GameCatalogDatasets.Equipment] = GameCatalogHashing.ComputeCanonicalJsonHash(equipmentViews, JsonOptions),
            [GameCatalogDatasets.CampaignBattles] = GameCatalogHashing.ComputeCanonicalJsonHash(campaignBattleViews, JsonOptions),
            [GameCatalogDatasets.CampaignDefinitions] = GameCatalogHashing.ComputeCanonicalJsonHash(campaignDefinitionViews, JsonOptions),
            [GameCatalogDatasets.Lres] = GameCatalogHashing.ComputeCanonicalJsonHash(lreViews, JsonOptions),
        };

        return new GameCatalogSnapshot(
            manifest.Version,
            manifest.SchemaVersion,
            manifest.GameVersion,
            GameCatalogHashing.ComputeSnapshotHash(manifest.Version, manifest.SchemaVersion, manifest.GameVersion, datasetHashes),
            new ReadOnlyDictionary<string, string>(datasetHashes),
            new ReadOnlyDictionary<string, GameCatalogFactionUnits>(unitsByFaction),
            new ReadOnlyCollection<GameCatalogMowUpgradeCost>(mowUpgradeCosts.ToArray()),
            new ReadOnlyCollection<GameCatalogEquipmentUpgradeCost>(equipmentUpgradeCosts.ToArray()),
            new ReadOnlyDictionary<string, GameCatalogFactionNpcs>(npcsByFaction),
            new ReadOnlyDictionary<string, IReadOnlyList<GameCatalogEquipment>>(equipmentByType),
            new ReadOnlyDictionary<string, IReadOnlyList<GameCatalogUpgrade>>(upgradesByRarity),
            new ReadOnlyDictionary<string, GameCatalogCampaignGroup>(campaignGroups),
            new ReadOnlyCollection<GameCatalogDropChance>(dropChances.ToArray()),
            new ReadOnlyDictionary<string, GameCatalogLre>(lresByEvent),
            characterViews,
            npcList,
            mowList,
            upgradeViews,
            equipmentViews,
            campaignBattleViews,
            campaignDefinitionViews,
            lreViews
        );
    }

    private static T LoadDataset<T>(
        IReadOnlyDictionary<string, GameCatalogDatasetSource> datasets,
        string key
    )
    {
        if (!datasets.TryGetValue(key, out var dataset))
        {
            throw new InvalidOperationException($"GameCatalog manifest must include the '{key}' dataset.");
        }

        if (string.IsNullOrWhiteSpace(dataset.File))
        {
            throw new InvalidOperationException($"GameCatalog manifest dataset '{key}' must include a source file.");
        }

        using var document = ReadEmbeddedJsonDocument(dataset.File);
        return document.RootElement.Deserialize<T>(JsonOptions)
            ?? throw new InvalidOperationException($"GameCatalog dataset '{key}' is empty.");
    }

    private static JsonDocument ReadEmbeddedJsonDocument(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // Manifest file paths may include subfolders (e.g. "units/units-ultramarines.json").
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

    private sealed record GameCatalogManifestSource(
        string Version,
        int SchemaVersion,
        string GameVersion,
        IReadOnlyList<GameCatalogDatasetSource> Datasets
    );

    private sealed record GameCatalogDatasetSource(
        string Key,
        string File
    );
}
