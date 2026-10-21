using TacticusPlanner.Api.Features.UpstreamRateLimiting;

namespace TacticusPlanner.Api.Tests;

public sealed class UpstreamRateLimiterTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Fact]
    public void RejectsOnceThePermitsAreSpentAndReportsTheRemainingWindowRoundedUp()
    {
        var time = new ManualTime();
        var limiter = new UpstreamRateLimiter(time);
        Assert.All(Enumerable.Range(0, 2), _ => Assert.Null(limiter.TryAcquire("a", "POST", "x", 2, Minute)));

        time.Now += Minute - TimeSpan.FromSeconds(1.2);

        Assert.Equal(2, limiter.TryAcquire("a", "POST", "x", 2, Minute));
    }

    [Fact]
    public void RetryAfterIsAtLeastOneSecond()
    {
        var time = new ManualTime();
        var limiter = new UpstreamRateLimiter(time);
        Assert.Null(limiter.TryAcquire("a", "POST", "x", 1, Minute));

        time.Now += Minute - TimeSpan.FromMilliseconds(1);

        Assert.Equal(1, limiter.TryAcquire("a", "POST", "x", 1, Minute));
    }

    [Fact]
    public void RenewedWindowIsEvaluatedAtTheExactResetBoundary()
    {
        var time = new ManualTime();
        var limiter = new UpstreamRateLimiter(time);
        Assert.Null(limiter.TryAcquire("a", "POST", "x", 2, Minute));
        time.Now += TimeSpan.FromSeconds(30);
        Assert.Null(limiter.TryAcquire("a", "POST", "x", 2, Minute));
        Assert.NotNull(limiter.TryAcquire("a", "POST", "x", 2, Minute));

        // Windows are anchored at the first accepted request, not at the later ones.
        time.Now += TimeSpan.FromSeconds(30);

        Assert.Null(limiter.TryAcquire("a", "POST", "x", 2, Minute));
        Assert.Null(limiter.TryAcquire("a", "POST", "x", 2, Minute));
        Assert.NotNull(limiter.TryAcquire("a", "POST", "x", 2, Minute));
    }

    [Fact]
    public void UnusedPermitsDoNotCarryOver()
    {
        var time = new ManualTime();
        var limiter = new UpstreamRateLimiter(time);
        Assert.Null(limiter.TryAcquire("a", "POST", "x", 2, Minute));
        time.Now += Minute * 5;

        Assert.Null(limiter.TryAcquire("a", "POST", "x", 2, Minute));
        Assert.Null(limiter.TryAcquire("a", "POST", "x", 2, Minute));
        Assert.NotNull(limiter.TryAcquire("a", "POST", "x", 2, Minute));
    }

    [Fact]
    public void BucketsAreIsolatedByIdentityMethodAndEndpoint()
    {
        var limiter = new UpstreamRateLimiter(new ManualTime());
        Assert.Null(limiter.TryAcquire("a", "POST", "x", 1, Minute));

        Assert.NotNull(limiter.TryAcquire("a", "POST", "x", 1, Minute));
        Assert.Null(limiter.TryAcquire("b", "POST", "x", 1, Minute));
        Assert.Null(limiter.TryAcquire("a", "PUT", "x", 1, Minute));
        Assert.Null(limiter.TryAcquire("a", "POST", "y", 1, Minute));
    }
}
