using System.Text.Json;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog;

/// <summary>Writes a compilation as the compiled catalog the runtime embeds (see <see cref="GameCatalogCompiledFiles"/>).</summary>
public static class GameCatalogCompiledWriter
{
    /// <summary>Writes the manifest, every served dataset except <c>events-calendar</c>, and the runtime data under
    /// <paramref name="outputDirectory"/>, replacing what was there.</summary>
    public static void Write(GameCatalogCompilation compilation, string outputDirectory)
    {
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }

        Directory.CreateDirectory(Path.Join(outputDirectory, Path.GetDirectoryName(GameCatalogCompiledFiles.Dataset("x"))!));

        var views = compilation.ToServedViews();
        foreach (var (key, propertyName) in GameCatalogServedViews.PropertiesByDataset)
        {
            var property = typeof(GameCatalogServedViews).GetProperty(propertyName)!;
            WriteJson(outputDirectory, GameCatalogCompiledFiles.Dataset(key), property.GetValue(views), property.PropertyType);
        }

        var manifest = new GameCatalogCompiledManifest(
            compilation.Version,
            compilation.SchemaHash,
            compilation.GameVersion,
            compilation.DatasetHashes
                .Where(pair => GameCatalogServedViews.PropertiesByDataset.ContainsKey(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        WriteJson(outputDirectory, GameCatalogCompiledFiles.Runtime, compilation.ToRuntimeData(), typeof(GameCatalogRuntimeData));
        // The manifest last: the build treats it as the output that says the compile finished.
        WriteJson(outputDirectory, GameCatalogCompiledFiles.Manifest, manifest, typeof(GameCatalogCompiledManifest));
    }

    private static void WriteJson(string outputDirectory, string relativePath, object? value, Type type)
    {
        using var stream = File.Create(Path.Join(outputDirectory, relativePath));
        JsonSerializer.Serialize(stream, value, type, GameCatalogCompiledFiles.JsonOptions);
    }
}
