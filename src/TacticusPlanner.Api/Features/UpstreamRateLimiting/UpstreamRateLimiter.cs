namespace TacticusPlanner.Api.Features.UpstreamRateLimiting;

/// <summary>
/// Fixed-window limiter keyed by <c>(identity, HTTP method, endpoint)</c>. A bucket's windows are
/// non-overlapping, anchored at its first accepted request, with no queue and no carry-over: permits renew
/// at <c>t0 + k * window</c>. State is per process.
/// </summary>
public sealed class UpstreamRateLimiter(TimeProvider time)
{
    private const int SweepEvery = 1024;

    // ponytail: one global lock, per-bucket locks if these six low-volume endpoints ever contend.
    private readonly Lock gate = new();
    private readonly Dictionary<(string Identity, string Method, string Endpoint), Bucket> buckets = [];
    private int calls;

    /// <summary>Null when the request may proceed; otherwise the <c>Retry-After</c> delay in whole seconds.</summary>
    public int? TryAcquire(string identity, string method, string endpoint, int permits, TimeSpan window)
    {
        var now = time.GetUtcNow();
        lock (gate)
        {
            if (++calls % SweepEvery == 0)
            {
                Sweep(now);
            }

            var key = (identity, method, endpoint);
            if (!buckets.TryGetValue(key, out var bucket))
            {
                buckets[key] = new Bucket(now, window) { Count = 1 };
                return null;
            }

            // The renewed window is evaluated before deciding to reject, including exactly at its boundary.
            var elapsedWindows = (long)((now - bucket.Start) / window);
            if (elapsedWindows > 0)
            {
                bucket.Start += TimeSpan.FromTicks(window.Ticks * elapsedWindows);
                bucket.Count = 0;
            }

            if (bucket.Count < permits)
            {
                bucket.Count++;
                return null;
            }

            return (int)Math.Max(1, Math.Ceiling((bucket.Start + window - now).TotalSeconds));
        }
    }

    // A bucket whose current window has ended holds no state worth keeping: its next request starts afresh.
    private void Sweep(DateTimeOffset now)
    {
        foreach (var expired in buckets.Where(pair => now >= pair.Value.Start + pair.Value.Window).Select(pair => pair.Key).ToList())
        {
            buckets.Remove(expired);
        }
    }

    private sealed class Bucket(DateTimeOffset start, TimeSpan window)
    {
        public DateTimeOffset Start { get; set; } = start;

        public TimeSpan Window { get; } = window;

        public int Count { get; set; }
    }
}
