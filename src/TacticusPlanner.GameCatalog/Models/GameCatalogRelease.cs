namespace TacticusPlanner.GameCatalog.Models;

/// <summary>
/// Release metadata for the embedded game catalog. These travel with the embedded data (the source
/// files are discovered by convention, so there is no separate manifest file) and are bumped whenever
/// the catalog data or its denormalized shape changes.
/// </summary>
public static class GameCatalogRelease
{
    /// <summary>Human-readable release tag for the embedded snapshot.</summary>
    public const string Version = "dev-2026-06-26";

    /// <summary>Denormalized payload schema version; bump when a served dataset's shape changes.</summary>
    public const int SchemaVersion = 14;

    /// <summary>The in-game data version the embedded catalog was extracted from.</summary>
    public const string GameVersion = "1.40";
}
