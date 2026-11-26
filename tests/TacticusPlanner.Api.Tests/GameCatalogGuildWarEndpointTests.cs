using System.Net;
using System.Text.Json.Nodes;

namespace TacticusPlanner.Api.Tests;

public sealed class GameCatalogGuildWarEndpointTests(GameCatalogApiFactory factory) : IClassFixture<GameCatalogApiFactory>
{
    [Fact]
    public async Task ServesOneRecordPerLiveSeasonWithItsSlotCaps()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/game-catalog/guild-war");
        request.Headers.Add(TestAuthenticationHandler.NoAuthHeader, "1");

        var response = await factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        var seasons = envelope["data"]!.AsArray();
        Assert.Equal(
            ["guildWarSeasonA", "guildWarSeasonB", "guildWarSeasonC", "guildWarSeasonD"],
            seasons.Select(node => node!["id"]!.GetValue<string>()));
        var hero = seasons[0]!["tiers"]!.AsArray().Single(node => node!["id"]!.GetValue<string>() == "Hero")!;
        Assert.Equal(
            ["Mythic", "Mythic", "Legendary", "Legendary", "Legendary"],
            hero["rarityCaps"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Null(seasons[0]!["levels"]![0]!["scoreFactorPercent"]);
    }
}
