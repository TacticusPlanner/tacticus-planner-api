using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;

namespace TacticusPlanner.Catalog;

internal sealed class EmbeddedCatalogProvider : ICatalogProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public EmbeddedCatalogProvider()
    {
        Current = LoadSnapshot();
    }

    public CatalogSnapshot Current { get; }

    private static CatalogSnapshot LoadSnapshot()
    {
        using var manifestDocument = ReadEmbeddedJsonDocument("catalog-manifest.json");
        var manifest = manifestDocument.RootElement.Deserialize<CatalogManifestSource>(JsonOptions)
            ?? throw new InvalidOperationException("Catalog manifest is empty.");

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new InvalidOperationException("Catalog manifest version is required.");
        }

        if (manifest.SchemaVersion < 1)
        {
            throw new InvalidOperationException("Catalog manifest schema version must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(manifest.GameVersion))
        {
            throw new InvalidOperationException("Catalog manifest game version is required.");
        }

        var datasets = manifest.Datasets.ToDictionary(dataset => dataset.Key, StringComparer.Ordinal);

        // ---- load raw source collections -------------------------------------------------------
        var unitsByFaction = new Dictionary<string, CatalogFactionUnits>(StringComparer.Ordinal);
        foreach (var key in CatalogDatasets.UnitFactions)
        {
            unitsByFaction[key] = LoadDataset<CatalogFactionUnits>(datasets, key);
        }

        var mowUpgradeCosts = LoadDataset<IReadOnlyList<CatalogMowUpgradeCost>>(datasets, CatalogDatasets.MowUpgradeCosts);
        var equipmentUpgradeCosts = LoadDataset<IReadOnlyList<CatalogEquipmentUpgradeCost>>(datasets, CatalogDatasets.EquipmentUpgradeCosts);
        var dropChances = LoadDataset<IReadOnlyList<CatalogDropChance>>(datasets, CatalogDatasets.DropChances);

        var npcsByFaction = new Dictionary<string, CatalogFactionNpcs>(StringComparer.Ordinal);
        foreach (var key in CatalogDatasets.NpcFactions)
        {
            npcsByFaction[key] = LoadDataset<CatalogFactionNpcs>(datasets, key);
        }

        var equipmentByType = new Dictionary<string, IReadOnlyList<CatalogEquipment>>(StringComparer.Ordinal);
        foreach (var key in CatalogDatasets.EquipmentTypes)
        {
            equipmentByType[key] = LoadDataset<IReadOnlyList<CatalogEquipment>>(datasets, key);
        }

        var upgradesByRarity = new Dictionary<string, IReadOnlyList<CatalogUpgrade>>(StringComparer.Ordinal);
        foreach (var key in CatalogDatasets.UpgradeRarities)
        {
            upgradesByRarity[key] = LoadDataset<IReadOnlyList<CatalogUpgrade>>(datasets, key);
        }

        var campaignGroups = new Dictionary<string, CatalogCampaignGroup>(StringComparer.Ordinal);
        foreach (var key in CatalogDatasets.CampaignBattleGroups)
        {
            campaignGroups[key] = LoadDataset<CatalogCampaignGroup>(datasets, key);
        }

        var lresByEvent = new Dictionary<string, CatalogLre>(StringComparer.Ordinal);
        foreach (var key in CatalogDatasets.LreEvents)
        {
            lresByEvent[key] = LoadDataset<CatalogLre>(datasets, key);
        }

        // ---- build denormalized served datasets ------------------------------------------------
        var characterViews = CatalogDenormalizer.BuildCharacters(unitsByFaction, equipmentByType, campaignGroups, dropChances);
        var npcList = CatalogDenormalizer.BuildNpcs(npcsByFaction);
        var mowDataset = CatalogDenormalizer.BuildMows(unitsByFaction, mowUpgradeCosts);
        var upgradeViews = CatalogDenormalizer.BuildUpgrades(upgradesByRarity, campaignGroups, dropChances);
        var equipmentDataset = CatalogDenormalizer.BuildEquipment(equipmentByType, equipmentUpgradeCosts);
        var campaignGroupViews = CatalogDenormalizer.BuildCampaignGroups(campaignGroups, dropChances);
        var lreViews = CatalogDenormalizer.BuildLres(lresByEvent, unitsByFaction);

        // Served dataset hashes are computed over the canonical JSON of each denormalized payload.
        var datasetHashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CatalogDatasets.Characters] = CatalogHashing.ComputeCanonicalJsonHash(characterViews, JsonOptions),
            [CatalogDatasets.Npcs] = CatalogHashing.ComputeCanonicalJsonHash(npcList, JsonOptions),
            [CatalogDatasets.Mows] = CatalogHashing.ComputeCanonicalJsonHash(mowDataset, JsonOptions),
            [CatalogDatasets.Upgrades] = CatalogHashing.ComputeCanonicalJsonHash(upgradeViews, JsonOptions),
            [CatalogDatasets.Equipment] = CatalogHashing.ComputeCanonicalJsonHash(equipmentDataset, JsonOptions),
            [CatalogDatasets.CampaignBattles] = CatalogHashing.ComputeCanonicalJsonHash(campaignGroupViews, JsonOptions),
            [CatalogDatasets.Lres] = CatalogHashing.ComputeCanonicalJsonHash(lreViews, JsonOptions),
        };

        return new CatalogSnapshot(
            manifest.Version,
            manifest.SchemaVersion,
            manifest.GameVersion,
            CatalogHashing.ComputeSnapshotHash(manifest.Version, manifest.SchemaVersion, manifest.GameVersion, datasetHashes),
            new ReadOnlyDictionary<string, string>(datasetHashes),
            new ReadOnlyDictionary<string, CatalogFactionUnits>(unitsByFaction),
            new ReadOnlyCollection<CatalogMowUpgradeCost>(mowUpgradeCosts.ToArray()),
            new ReadOnlyCollection<CatalogEquipmentUpgradeCost>(equipmentUpgradeCosts.ToArray()),
            new ReadOnlyDictionary<string, CatalogFactionNpcs>(npcsByFaction),
            new ReadOnlyDictionary<string, IReadOnlyList<CatalogEquipment>>(equipmentByType),
            new ReadOnlyDictionary<string, IReadOnlyList<CatalogUpgrade>>(upgradesByRarity),
            new ReadOnlyDictionary<string, CatalogCampaignGroup>(campaignGroups),
            new ReadOnlyCollection<CatalogDropChance>(dropChances.ToArray()),
            new ReadOnlyDictionary<string, CatalogLre>(lresByEvent),
            characterViews,
            npcList,
            mowDataset,
            upgradeViews,
            equipmentDataset,
            campaignGroupViews,
            lreViews
        );
    }

    private static T LoadDataset<T>(
        IReadOnlyDictionary<string, CatalogDatasetSource> datasets,
        string key
    )
    {
        if (!datasets.TryGetValue(key, out var dataset))
        {
            throw new InvalidOperationException($"Catalog manifest must include the '{key}' dataset.");
        }

        if (string.IsNullOrWhiteSpace(dataset.File))
        {
            throw new InvalidOperationException($"Catalog manifest dataset '{key}' must include a source file.");
        }

        using var document = ReadEmbeddedJsonDocument(dataset.File);
        return document.RootElement.Deserialize<T>(JsonOptions)
            ?? throw new InvalidOperationException($"Catalog dataset '{key}' is empty.");
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
            throw new InvalidOperationException($"Embedded catalog data file '{fileName}' was not found.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded catalog data file '{fileName}' could not be opened.");

        return JsonDocument.Parse(stream);
    }

    private sealed record CatalogManifestSource(
        string Version,
        int SchemaVersion,
        string GameVersion,
        IReadOnlyList<CatalogDatasetSource> Datasets
    );

    private sealed record CatalogDatasetSource(
        string Key,
        string File
    );
}
