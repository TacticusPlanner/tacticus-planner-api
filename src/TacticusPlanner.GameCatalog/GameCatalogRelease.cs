namespace TacticusPlanner.GameCatalog.Models;

/// <summary>Release metadata of the embedded compiled catalog, derived from the raw data at build time (nothing is
/// bumped by hand). The served schema hash is derived separately from the payload types.</summary>
public static class GameCatalogRelease
{
    /// <summary>The game build the pinned datamine was extracted from (its <c>raw/VERSION</c>, e.g.
    /// <c>1.43.157.2989</c>).</summary>
    public static string GameVersion => CompiledGameCatalog.Embedded.Manifest.GameVersion;

    /// <summary>Release tag of the embedded catalog: <see cref="GameVersion"/> plus a short digest of every raw data
    /// file, e.g. <c>1.43.157.2989+3f2a9c01b7de</c>. Any datamine or curated change moves it; a code-only change does
    /// not.</summary>
    public static string Version => CompiledGameCatalog.Embedded.Manifest.Version;
}
