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

        var datasets = manifest.Datasets.ToDictionary(dataset => dataset.Key, StringComparer.Ordinal);
        var datasetHashes = new Dictionary<string, string>(StringComparer.Ordinal);

        var units = LoadDataset<IReadOnlyList<CatalogUnit>>(datasets, CatalogDatasets.Units, datasetHashes);
        var mows = LoadDataset<CatalogMowsDataset>(datasets, CatalogDatasets.Mows, datasetHashes);
        var upgrades = LoadDataset<IReadOnlyList<CatalogUpgrade>>(datasets, CatalogDatasets.Upgrades, datasetHashes);
        var equipment = LoadDataset<IReadOnlyList<CatalogEquipment>>(datasets, CatalogDatasets.Equipment, datasetHashes);
        var campaigns = LoadDataset<IReadOnlyList<CatalogCampaign>>(datasets, CatalogDatasets.Campaigns, datasetHashes);
        var campaignEvents = LoadDataset<IReadOnlyList<CatalogCampaign>>(
            datasets,
            CatalogDatasets.CampaignEvents,
            datasetHashes
        );
        var campaignBattles = LoadDataset<IReadOnlyList<CatalogCampaignBattle>>(
            datasets,
            CatalogDatasets.CampaignBattles,
            datasetHashes
        );
        var lres = LoadDataset<IReadOnlyList<CatalogLre>>(datasets, CatalogDatasets.Lres, datasetHashes);

        return new CatalogSnapshot(
            manifest.Version,
            manifest.SchemaVersion,
            CatalogHashing.ComputeSnapshotHash(manifest.Version, manifest.SchemaVersion, datasetHashes),
            new ReadOnlyDictionary<string, string>(datasetHashes),
            new ReadOnlyCollection<CatalogUnit>(units.ToArray()),
            new ReadOnlyCollection<CatalogMow>(mows.Mows.ToArray()),
            new ReadOnlyCollection<CatalogMowUpgradeCost>(mows.UpgradeCosts.ToArray()),
            new ReadOnlyCollection<CatalogUpgrade>(upgrades.ToArray()),
            new ReadOnlyCollection<CatalogEquipment>(equipment.ToArray()),
            new ReadOnlyCollection<CatalogCampaign>(campaigns.ToArray()),
            new ReadOnlyCollection<CatalogCampaign>(campaignEvents.ToArray()),
            new ReadOnlyCollection<CatalogCampaignBattle>(campaignBattles.ToArray()),
            new ReadOnlyCollection<CatalogLre>(lres.ToArray())
        );
    }

    private static T LoadDataset<T>(
        IReadOnlyDictionary<string, CatalogDatasetSource> datasets,
        string key,
        Dictionary<string, string> datasetHashes
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
        var value = document.RootElement.Deserialize<T>(JsonOptions)
            ?? throw new InvalidOperationException($"Catalog dataset '{key}' is empty.");

        datasetHashes[key] = CatalogHashing.ComputeCanonicalJsonHash(value, JsonOptions);

        return value;
    }

    private static JsonDocument ReadEmbeddedJsonDocument(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith($".Data.{fileName}", StringComparison.Ordinal));

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
        IReadOnlyList<CatalogDatasetSource> Datasets
    );

    private sealed record CatalogDatasetSource(
        string Key,
        string File
    );
}
