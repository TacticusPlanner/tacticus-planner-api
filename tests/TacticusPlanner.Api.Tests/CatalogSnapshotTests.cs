using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// Lightweight, dependency-free snapshot tests guarding the public catalog surface. The manifest
/// registry snapshot (keys + urls + versions, hashes excluded) guards the dataset/route layout; the
/// lres body snapshot guards the denormalized payload shape. Regenerate baselines by running with the
/// environment variable <c>UPDATE_SNAPSHOTS=1</c>.
/// </summary>
public sealed class CatalogSnapshotTests : IClassFixture<CatalogApiFactory>
{
    private readonly CatalogApiFactory factory;

    public CatalogSnapshotTests(CatalogApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ManifestRegistryMatchesSnapshot()
    {
        var client = factory.CreateClient();
        var json = await client.GetStringAsync("/api/v1/catalog/manifest", TestContext.Current.CancellationToken);
        using var manifest = JsonDocument.Parse(json);
        var root = manifest.RootElement;

        // Registry only: schema/game version + sorted {key,url}. Volatile hashes are intentionally dropped.
        var registry = new JsonObject
        {
            ["schemaVersion"] = root.GetProperty("schemaVersion").GetInt32(),
            ["gameVersion"] = root.GetProperty("gameVersion").GetString(),
            ["datasets"] = new JsonArray(root.GetProperty("datasets").EnumerateArray()
                .Select(dataset => (JsonNode)new JsonObject
                {
                    ["key"] = dataset.GetProperty("key").GetString(),
                    ["url"] = dataset.GetProperty("url").GetString(),
                })
                .ToArray()),
        };

        AssertMatchesSnapshot("catalog-manifest-registry.json", registry.ToJsonString(Indented));
    }

    [Fact]
    public async Task LresDatasetMatchesSnapshot()
    {
        var client = factory.CreateClient();
        var json = await client.GetStringAsync("/api/v1/catalog/lres", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.GetProperty("data");

        AssertMatchesSnapshot("catalog-lres.json", JsonSerializer.Serialize(data, Indented));
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static void AssertMatchesSnapshot(string fileName, string actual, [CallerFilePath] string? callerPath = null)
    {
        var directory = Path.Combine(Path.GetDirectoryName(callerPath)!, "__snapshots__");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        var normalized = actual.Replace("\r\n", "\n");

        var update = Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") is "1" or "true";
        if (update || !File.Exists(path))
        {
            File.WriteAllText(path, normalized, new UTF8Encoding(false));
        }

        var expected = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.Equal(expected, normalized);
    }
}
