using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TacticusPlanner.Api.Tests;

public sealed class HealthEndpointCachingTests(GameCatalogApiFactory factory) : IClassFixture<GameCatalogApiFactory>
{
    [Fact]
    public async Task ReadinessIsCachedForAllCallersWhileLivenessIsNot()
    {
        var ct = TestContext.Current.CancellationToken;
        var check = new CountingHealthCheck();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            // Replace the real checks (the database one needs Postgres) with one counted check, tagged live so
            // both endpoints run it.
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                options.Registrations.Clear();
                options.Registrations.Add(new HealthCheckRegistration("counting", check, null, ["live"]));
            })));
        var client = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready", ct)).StatusCode);
        // A query string or Authorization header must not bypass the cache.
        using var busting = new HttpRequestMessage(HttpMethod.Get, "/health/ready?nocache=1");
        busting.Headers.Authorization = new("Bearer", "anything");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(busting, ct)).StatusCode);
        Assert.Equal(1, check.Executions);

        await client.GetAsync("/health/live", ct);
        await client.GetAsync("/health/live", ct);
        Assert.Equal(3, check.Executions);
    }

    private sealed class CountingHealthCheck : IHealthCheck
    {
        private int executions;

        public int Executions => executions;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref executions);
            return Task.FromResult(HealthCheckResult.Healthy());
        }
    }
}
