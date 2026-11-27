using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog;

/// <summary>
/// Holds the immutable catalog snapshot read from the catalog compiled into this assembly. The compiled catalog is
/// read when the singleton is first resolved (resolve it during app startup). When the UTC day changes the snapshot is
/// replaced by one with that day's events calendar (<see cref="GameCatalogLoader.Load(DateTimeOffset)"/> re-projects
/// only the calendar; every other served payload is shared), so every replica serves the same snapshot (and dataset
/// hashes) on a given day.
/// </summary>
internal sealed class EmbeddedGameCatalogProvider : IGameCatalogProvider
{
    private readonly TimeProvider time;
    private readonly Lock reloadLock = new();
    private volatile Loaded loaded;

    public EmbeddedGameCatalogProvider(TimeProvider time)
    {
        this.time = time;
        var now = time.GetUtcNow();
        loaded = new Loaded(now.UtcDateTime.Date, GameCatalogLoader.Load(now));
    }

    public GameCatalogSnapshot Current
    {
        get
        {
            var now = time.GetUtcNow();
            var current = loaded;
            if (current.Day == now.UtcDateTime.Date)
            {
                return current.Snapshot;
            }

            lock (reloadLock)
            {
                if (loaded.Day != now.UtcDateTime.Date)
                {
                    loaded = new Loaded(now.UtcDateTime.Date, GameCatalogLoader.Load(now));
                }

                return loaded.Snapshot;
            }
        }
    }

    private sealed record Loaded(DateTime Day, GameCatalogSnapshot Snapshot);
}
