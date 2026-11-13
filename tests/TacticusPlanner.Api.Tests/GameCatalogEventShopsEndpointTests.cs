using System.Net;
using System.Text.Json.Nodes;

namespace TacticusPlanner.Api.Tests;

public sealed class GameCatalogEventShopsEndpointTests(GameCatalogApiFactory factory) : IClassFixture<GameCatalogApiFactory>
{
    [Fact]
    public async Task ServesTheDatedEventShopsInStartOrder()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/game-catalog/event-shops");
        request.Headers.Add(TestAuthenticationHandler.NoAuthHeader, "1");

        var response = await factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        var events = envelope["data"]!.AsArray();
        Assert.Equal(["june-2026", "august-2026", "october-2026"], events.Select(node => node!["id"]!.GetValue<string>()));
        var october = events[2]!;
        Assert.Equal("seasonalEventCurrencyOctober2026", october["currency"]!.GetValue<string>());
        Assert.Equal(7, october["dayCount"]!.GetValue<int>());
        Assert.Null(october["weeks"]![0]!["estimatedWeeklyIncome"]);
    }
}
