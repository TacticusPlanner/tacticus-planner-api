namespace TacticusPlanner.GameCatalog.Models;

/// <summary>
/// Release metadata for the embedded game catalog. These travel with the embedded data (the source
/// files are discovered by convention, so there is no separate manifest file) and are bumped whenever
/// the catalog data changes. The served schema hash (see <c>GameCatalogContractHashing</c>) and game
/// version (the pinned datamine's <c>raw/VERSION</c>) are derived instead, so neither is bumped here.
/// </summary>
public static class GameCatalogRelease
{
    /// <summary>Human-readable release tag for the embedded snapshot.</summary>
    public const string Version = "dev-2026-10-19.1";
}
