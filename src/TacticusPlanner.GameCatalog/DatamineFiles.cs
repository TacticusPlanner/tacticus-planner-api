using System.Reflection;

namespace TacticusPlanner.GameCatalog;

/// <summary>
/// The raw game config files copied from <c>tacticus-planner-data-mines</c> (<c>Data/datamine/raw/</c>, pinned by
/// <c>Data/datamine/REF</c>, refreshed by <c>scripts/pull-datamine.cs</c>). They are embedded under their upstream
/// <c>raw/</c> path, so a denormalizer names exactly the file it reads. Only paths listed in
/// <c>Data/datamine/files.txt</c> are copied.
/// </summary>
public static class DatamineFiles
{
    /// <summary>Resource-name prefix of every datamine copy; the leaf-name convention loader skips these.</summary>
    public const string ResourcePrefix = "datamine/";

    /// <summary>Opens the pinned copy of <c>raw/{rawPath}</c>, e.g. <c>gameconfig/items.json</c>.</summary>
    /// <exception cref="InvalidOperationException">The path is not allowlisted (so not embedded).</exception>
    public static Stream Open(string rawPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPath);

        var resourceName = ResourcePrefix + rawPath.Replace('\\', '/');
        return Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Datamine file 'raw/{rawPath}' is not embedded. List it in Data/datamine/files.txt and run scripts/pull-datamine.cs.");
    }

    /// <summary>The raw paths of every embedded datamine copy, sorted ordinally.</summary>
    public static IReadOnlyList<string> RawPaths { get; } = Assembly.GetExecutingAssembly()
        .GetManifestResourceNames()
        .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
        .Select(name => name[ResourcePrefix.Length..])
        .Order(StringComparer.Ordinal)
        .ToArray();
}
