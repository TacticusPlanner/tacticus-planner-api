namespace TacticusPlanner.GameCatalog.Models;

/// <summary>
/// Release metadata for the embedded game catalog. These travel with the embedded data (the source
/// files are discovered by convention, so there is no separate manifest file) and are bumped whenever
/// the catalog data changes. The served schema hash is derived from the payload types instead (see
/// <c>GameCatalogContractHashing</c>), so a shape change needs no bump here.
/// </summary>
public static class GameCatalogRelease
{
    /// <summary>Human-readable release tag for the embedded snapshot.</summary>
    public const string Version = "dev-2026-10-19.1";

    /// <summary>The in-game data version the embedded catalog was extracted from.</summary>
    public const string GameVersion = "1.43";
}
