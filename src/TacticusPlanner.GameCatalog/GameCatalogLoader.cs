using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog;

/// <summary>
/// Loads the lean <see cref="GameCatalogSnapshot"/> the API reads from the catalog compiled into this assembly at
/// build time (<see cref="CompiledGameCatalog"/>). Nothing is denormalized, validated or hashed here except the day's
/// events calendar: the build already did the rest, and fails when the raw data does not validate.
/// </summary>
public static class GameCatalogLoader
{
    /// <summary>Loads the catalog as of now.</summary>
    public static GameCatalogSnapshot Load() => Load(DateTimeOffset.UtcNow);

    /// <summary>Loads the catalog as of <paramref name="asOf"/>'s UTC day (the events calendar starts at that day).</summary>
    public static GameCatalogSnapshot Load(DateTimeOffset asOf) => CompiledGameCatalog.Embedded.SnapshotFor(asOf);
}
