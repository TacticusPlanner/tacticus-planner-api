using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.FeatureFlags;

namespace TacticusPlanner.Api.Tests;

public sealed class FeatureFlagsEndpointTests(GameCatalogApiFactory factory) : IClassFixture<GameCatalogApiFactory>
{
    [Fact]
    public async Task ListsOnlyEnabledFlagsToAnonymousCallers()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("FeatureManagement:Zeta", "true");
            builder.UseSetting("FeatureManagement:Alpha", "true");
            builder.UseSetting("FeatureManagement:Hidden", "false");
        });
        var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/feature-flags");
        request.Headers.Add(TestAuthenticationHandler.NoAuthHeader, "1");

        var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=60", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<FeatureFlagsResponse>(ct);
        Assert.Equal(["Alpha", "Zeta"], body!.Enabled);
    }

    [Fact]
    public async Task ListsNothingWhenNoFlagIsConfigured()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/feature-flags");
        request.Headers.Add(TestAuthenticationHandler.NoAuthHeader, "1");

        var body = await (await client.SendAsync(request, ct)).Content.ReadFromJsonAsync<FeatureFlagsResponse>(ct);

        Assert.Empty(body!.Enabled);
    }
}
