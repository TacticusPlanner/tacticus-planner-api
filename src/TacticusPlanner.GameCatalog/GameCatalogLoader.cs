using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog;

/// <summary>
/// Loads the lean <see cref="GameCatalogSnapshot"/> the API reads. <see cref="Load(DateTimeOffset)"/> is public so app
/// startup (and tests) can eagerly load the catalog and fail fast on bad data.
/// </summary>
public static class GameCatalogLoader
{
    /// <summary>Loads the catalog as of now.</summary>
    /// <exception cref="InvalidOperationException">A source file is missing/empty or validation fails.</exception>
    public static GameCatalogSnapshot Load() => Load(DateTimeOffset.UtcNow);

    /// <summary>Loads the catalog as of <paramref name="asOf"/>'s UTC day (the events calendar starts at that day).</summary>
    /// <exception cref="InvalidOperationException">A source file is missing/empty or validation fails.</exception>
    public static GameCatalogSnapshot Load(DateTimeOffset asOf) => FromCompilation(GameCatalogCompiler.Compile(asOf));

    /// <summary>The lean runtime snapshot of a compilation: its release metadata, served payloads and runtime data.</summary>
    public static GameCatalogSnapshot FromCompilation(GameCatalogCompilation compilation) => new(
        compilation.Version,
        compilation.SchemaHash,
        compilation.GameVersion,
        compilation.SourceHash,
        compilation.DatasetHashes,
        compilation.ToServedViews(),
        compilation.EventsCalendar,
        compilation.ToRuntimeData());
}
