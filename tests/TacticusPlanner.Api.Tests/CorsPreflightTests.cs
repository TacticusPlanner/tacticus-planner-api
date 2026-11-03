using System.Net;

namespace TacticusPlanner.Api.Tests;

public sealed class CorsPreflightTests(GameCatalogApiFactory factory) : IClassFixture<GameCatalogApiFactory>
{
    private const string Origin = "https://new.tacticusplanner.app";

    [Fact]
    public async Task PreflightIsCacheableByTheBrowser()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("Cors:AllowedOrigins:0", Origin));
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/goals");
        request.Headers.Add("Origin", Origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");

        var response = await host.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(Origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("7200", Assert.Single(response.Headers.GetValues("Access-Control-Max-Age")));
    }
}
