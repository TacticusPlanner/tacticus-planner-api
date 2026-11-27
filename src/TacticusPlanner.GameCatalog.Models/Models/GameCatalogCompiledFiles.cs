using System.Text.Json;

namespace TacticusPlanner.GameCatalog.Models;

/// <summary>
/// The compiled catalog's files and JSON conventions: what the build-time compiler writes and the runtime embeds and
/// reads back. Every file is serialized with <see cref="JsonOptions"/>, the same options the dataset hashes are
/// computed with.
/// </summary>
public static class GameCatalogCompiledFiles
{
    /// <summary>The catalog's JSON options: web conventions, case-insensitive on read.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Release metadata and the dataset hashes (<see cref="GameCatalogCompiledManifest"/>).</summary>
    public const string Manifest = "manifest.json";

    /// <summary>The runtime data (<see cref="GameCatalogRuntimeData"/>).</summary>
    public const string Runtime = "runtime.json";

    /// <summary>The prefix of the compiled files' embedded resource names in the runtime assembly.</summary>
    public const string ResourcePrefix = "compiled/";

    /// <summary>One served dataset's payload.</summary>
    public static string Dataset(string key) => $"datasets/{key}.json";
}

/// <summary>The compiled release metadata, and the hash of every served dataset except the day-dependent
/// <c>events-calendar</c>, whose hash the runtime computes each day.</summary>
public sealed record GameCatalogCompiledManifest(
    string Version,
    string SchemaHash,
    string GameVersion,
    IReadOnlyDictionary<string, string> DatasetHashes);
