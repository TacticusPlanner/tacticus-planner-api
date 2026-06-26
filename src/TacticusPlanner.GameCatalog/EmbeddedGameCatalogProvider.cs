using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog;

/// <summary>
/// Holds the immutable catalog snapshot loaded from the embedded data. The load + validation runs eagerly
/// (via <see cref="GameCatalogLoader.Load"/>) when the singleton is first resolved; resolve it during app
/// startup to fail fast on bad data.
/// </summary>
internal sealed class EmbeddedGameCatalogProvider : IGameCatalogProvider
{
    public EmbeddedGameCatalogProvider()
    {
        Current = GameCatalogLoader.Load();
    }

    public GameCatalogSnapshot Current { get; }
}
