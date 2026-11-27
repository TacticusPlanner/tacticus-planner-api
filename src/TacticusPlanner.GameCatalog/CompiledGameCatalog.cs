using System.Reflection;
using System.Text.Json;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog;

/// <summary>
/// The compiled catalog embedded in this assembly by the build (<c>TacticusPlanner.GameCatalog.Compiler</c> writes
/// it): release metadata and dataset hashes, every served payload except the calendar, and the runtime data. Read
/// once per process; <see cref="SnapshotFor"/> adds the day's events calendar.
/// </summary>
public sealed class CompiledGameCatalog
{
    private static readonly Lazy<CompiledGameCatalog> EmbeddedCatalog = new(() => Read(OpenEmbedded));

    private CompiledGameCatalog(GameCatalogCompiledManifest manifest, GameCatalogServedViews views, GameCatalogRuntimeData runtime)
    {
        Manifest = manifest;
        Views = views;
        Runtime = runtime;
    }

    /// <summary>The catalog compiled into this assembly.</summary>
    public static CompiledGameCatalog Embedded => EmbeddedCatalog.Value;

    public GameCatalogCompiledManifest Manifest { get; }

    public GameCatalogServedViews Views { get; }

    public GameCatalogRuntimeData Runtime { get; }

    /// <summary>The snapshot for <paramref name="asOf"/>'s UTC day. Only the calendar is new; the served payloads are
    /// shared with every other day's snapshot.</summary>
    public GameCatalogSnapshot SnapshotFor(DateTimeOffset asOf) => GameCatalogSnapshot.ForDay(Manifest, Views, Runtime, asOf);

    private static CompiledGameCatalog Read(Func<string, Stream> open)
    {
        var manifest = ReadJson<GameCatalogCompiledManifest>(open, GameCatalogCompiledFiles.Manifest);
        var runtime = ReadJson<GameCatalogRuntimeData>(open, GameCatalogCompiledFiles.Runtime);

        // One payload per constructor parameter, matched to its dataset by property name.
        var datasetsByProperty = GameCatalogServedViews.PropertiesByDataset.ToDictionary(
            pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
        var constructor = typeof(GameCatalogServedViews).GetConstructors().Single();
        var payloads = constructor.GetParameters()
            .Select(parameter =>
            {
                var path = GameCatalogCompiledFiles.Dataset(datasetsByProperty[parameter.Name!]);
                using var stream = open(path);
                return JsonSerializer.Deserialize(stream, parameter.ParameterType, GameCatalogCompiledFiles.JsonOptions)
                    ?? throw new InvalidOperationException($"Compiled game catalog file '{path}' is empty.");
            })
            .ToArray();

        return new CompiledGameCatalog(manifest, (GameCatalogServedViews)constructor.Invoke(payloads), runtime);
    }

    private static T ReadJson<T>(Func<string, Stream> open, string path)
    {
        using var stream = open(path);
        return JsonSerializer.Deserialize<T>(stream, GameCatalogCompiledFiles.JsonOptions)
            ?? throw new InvalidOperationException($"Compiled game catalog file '{path}' is empty.");
    }

    private static Stream OpenEmbedded(string path) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(GameCatalogCompiledFiles.ResourcePrefix + path)
        ?? throw new InvalidOperationException(
            $"Compiled game catalog file '{path}' is not embedded. Build TacticusPlanner.GameCatalog to compile the catalog.");
}
