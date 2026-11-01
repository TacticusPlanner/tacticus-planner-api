using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog;

/// <summary>
/// Holds the immutable catalog snapshot loaded from the embedded data. The load + validation runs eagerly
/// (via <see cref="GameCatalogLoader.Load(DateTimeOffset)"/>) when the singleton is first resolved; resolve it
/// during app startup to fail fast on bad data. The snapshot is rebuilt when the UTC day changes, so the rolling
/// events calendar stays current and every replica serves the same snapshot (and dataset hashes) on a given day.
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

    // ponytail: rebuilds the whole catalog once a day to refresh the calendar; split the calendar out of the
    // snapshot if the rebuild ever gets slow enough to matter on the request that triggers it.
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
