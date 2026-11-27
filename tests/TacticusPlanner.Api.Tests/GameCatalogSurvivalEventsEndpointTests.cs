using System.Net;
using System.Text.Json.Nodes;

namespace TacticusPlanner.Api.Tests;

public sealed class GameCatalogSurvivalEventsEndpointTests(GameCatalogApiFactory factory) : IClassFixture<GameCatalogApiFactory>
{
    [Fact]
    public async Task ServesEachSurvivalEventWithItsRulesAndDates()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/game-catalog/survival-events");
        request.Headers.Add(TestAuthenticationHandler.NoAuthHeader, "1");

        var response = await factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        var events = envelope["data"]!.AsArray();
        Assert.Equal(
            ["crescendo-a", "crescendo-b", "december-2026", "may-2026", "september-2026"],
            events.Select(node => node!["id"]!.GetValue<string>()));
        var december = events.Single(node => node!["id"]!.GetValue<string>() == "december-2026")!;
        Assert.Equal("admecManipulus", december["featuredCharacterId"]!.GetValue<string>());
        Assert.Equal(10, december["rules"]!["maxCharacters"]!.GetValue<int>());
        Assert.Equal("2026-12-03T00:00:00+00:00", december["startUtc"]!.GetValue<string>());
        Assert.Null(events[0]!["startUtc"]);
    }
}
