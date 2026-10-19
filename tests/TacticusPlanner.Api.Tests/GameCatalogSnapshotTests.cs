using System.Net;
using System.Text.Json.Nodes;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// Snapshot test guarding the public game catalog manifest: it makes a real (anonymous) call to
/// <c>/api/v1/game-catalog/manifest</c> and verifies the full response — release metadata, source hash and
/// every dataset's key/hash/url — against a committed Verify snapshot. Any change to the served dataset
/// shape or content shifts a hash and trips this test. To accept a new baseline, review the <c>*.received.*</c>
/// file Verify writes and promote it to <c>*.verified.*</c> (your diff tool, or rename it).
/// </summary>
public sealed class GameCatalogSnapshotTests(GameCatalogApiFactory factory)
    : IClassFixture<GameCatalogApiFactory>
{
    [Fact]
    public async Task GameCatalogManifestMatchesSnapshot()
    {
        var client = factory.CreateClient();

        // The catalog is public: request it without auth to also prove anonymous access works.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/game-catalog/manifest");
        request.Headers.Add(TestAuthenticationHandler.NoAuthHeader, "1");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        await VerifyJson(ScrubTimeDependentHashes(json));
    }

    [Fact]
    public async Task TheSharedLreLadderIsNoLongerServed()
    {
        // Authenticated, so an unmatched route reads as 404 rather than the anonymous 401 fallback.
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/game-catalog/lre-common", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CharacterAbilityCostsAreServedAnonymouslyForLevels2To60()
    {
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/game-catalog/character-ability-costs");
        request.Headers.Add(TestAuthenticationHandler.NoAuthHeader, "1");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
        Assert.Equal("character-ability-costs", payload["datasetKey"]!.GetValue<string>());

        var data = payload["data"]!.AsArray();
        Assert.Equal(Enumerable.Range(2, 59), data.Select(row => row!["level"]!.GetValue<int>()));
        var first = data[0]!.AsObject();
        Assert.Equal(["level", "gold", "badges"], first.Select(property => property.Key));
        Assert.Equal("Common", first["badges"]!["rarity"]!.GetValue<string>());
        Assert.Equal(1, first["badges"]!["amount"]!.GetValue<int>());
    }

    [Fact]
    public async Task MythicQuestsAreServedAnonymouslyWithStructuredRewards()
    {
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/game-catalog/mythic-quests");
        request.Headers.Add(TestAuthenticationHandler.NoAuthHeader, "1");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
        Assert.Equal("mythic-quests", payload["datasetKey"]!.GetValue<string>());

        var data = payload["data"]!.AsArray();
        Assert.Equal(117, data.Count);
        var abaddon = data.Single(row => row!["unitId"]!.GetValue<string>() == "blackAbaddon")!.AsObject();
        Assert.Equal(["unitId", "quests"], abaddon.Select(property => property.Key));

        var quest = abaddon["quests"]![0]!.AsObject();
        Assert.Equal(["number", "tasks", "rewards"], quest.Select(property => property.Key));
        Assert.Equal(["locaKey", "target", "parameters"], quest["tasks"]![0]!.AsObject().Select(property => property.Key));
        var shards = quest["rewards"]![0]!.AsObject();
        Assert.Equal(["type", "amount", "unitId"], shards.Select(property => property.Key));
        Assert.Equal("MythicShards", shards["type"]!.GetValue<string>());
        Assert.Equal(4, shards["amount"]!.GetValue<int>());
    }

    [Fact]
    public async Task MowsCarryTheirMythicAbility()
    {
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/game-catalog/mows");
        request.Headers.Add(TestAuthenticationHandler.NoAuthHeader, "1");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["data"]!.AsArray();
        Assert.All(data, mow => Assert.Equal(["name"], mow!["mythicAbility"]!.AsObject().Select(property => property.Key)));
        Assert.Equal(
            "WisdomOfTheAncients",
            data.Single(mow => mow!["id"]!.GetValue<string>() == "ultraDreadnought")!["mythicAbility"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task HordeModeTiersAreServedAnonymouslyWithCharactersSortedById()
    {
        var payload = await GetAnonymousDataset("horde-mode-tiers");
        Assert.Equal("horde-mode-tiers", payload["datasetKey"]!.GetValue<string>());

        var record = Assert.Single(payload["data"]!.AsArray())!.AsObject();
        Assert.Equal(["id", "tiers", "characters"], record.Select(property => property.Key));
        Assert.Equal(["S", "A", "B", "C", "D"], record["tiers"]!.AsArray().Select(tier => tier!["tier"]!.GetValue<string>()));
        Assert.Equal(["tier", "multiplier"], record["tiers"]![0]!.AsObject().Select(property => property.Key));

        var unitIds = record["characters"]!.AsArray().Select(character => character!["unitId"]!.GetValue<string>()).ToArray();
        Assert.Equal(unitIds.Order(StringComparer.Ordinal), unitIds);
        // Every seeded character carries its own multiplier override alongside its tier; synergies (in
        // authoring order) only when the strategy notes name teammates, and relics only where seeded.
        Assert.All(
            record["characters"]!.AsArray(),
            character => Assert.Equal(
                ["unitId", "tier", "multiplier"],
                character!.AsObject().Select(property => property.Key).Where(key => key is not ("synergies" or "relics"))));
        var angrax = record["characters"]!.AsArray().Single(character => character!["unitId"]!.GetValue<string>() == "blackTerminator")!;
        Assert.Equal(["unitId", "tier", "multiplier", "synergies", "relics"], angrax.AsObject().Select(property => property.Key));
        Assert.Equal("R_Block_DaemonfleshPlate", angrax["relics"]![0]!["equipmentId"]!.GetValue<string>());
        Assert.Equal(
            ["deathRotbone", "worldTerminator", "blackAbaddon"],
            angrax["synergies"]!.AsArray().Select(synergy => synergy!.GetValue<string>()));
    }

    [Theory]
    [InlineData("votanUthar", new[] { "2026-03-08T00:00:00Z", "2026-10-04T00:00:00Z" })]
    [InlineData("tauFarsight", new[] { "2025-12-28T00:00:00Z", "2026-06-21T00:00:00Z" })]
    [InlineData("astarLysander", new[] { "2026-08-30T00:00:00Z" })]
    [InlineData("lostaBile", new[] { "2026-11-08T00:00:00Z" })]
    public async Task LresServeTheirRunStartsIndexedByRun(string id, string[] expected)
    {
        var payload = await GetAnonymousDataset("lres");

        var record = payload["data"]!.AsArray().Single(lre => lre!["id"]!.GetValue<string>() == id)!.AsObject();
        Assert.Equal(expected, record["eventStageStartDatesUtc"]!.AsArray().Select(start => start!.GetValue<string>()));
    }

    private async Task<JsonObject> GetAnonymousDataset(string datasetKey)
    {
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/game-catalog/{datasetKey}");
        request.Headers.Add(TestAuthenticationHandler.NoAuthHeader, "1");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
    }

    [Fact]
    public async Task GuildRaidMetaDatasetIsAnonymousAndIdOnly()
    {
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/game-catalog/guild-raid-meta");
        request.Headers.Add(TestAuthenticationHandler.NoAuthHeader, "1");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
        Assert.Equal("guild-raid-meta", payload["datasetKey"]!.GetValue<string>());

        var data = payload["data"]!.AsObject();
        Assert.Equal("terminus-maximus-and-cognitae-guild-raid-meta", data["sourceId"]!.GetValue<string>());
        Assert.Equal("2026-09-14", data["updatedOn"]!.GetValue<string>());
        Assert.NotEmpty(data["comps"]!.AsArray());
        Assert.NotEmpty(data["bosses"]!.AsArray());
        Assert.NotEmpty(data["primes"]!.AsArray());
        Assert.Null(data["sourceUrl"]);
        Assert.Null(data["displayName"]);

        // Companion `tacticus-planner-apps` schema contract: every recommendation carries a stable id, five
        // hero slots with id-only replacement rules, a positive efficiency, and an ordered MoW replacement
        // list; every boss group carries its ordered primeUnitSetIds.
        var bossGroup = data["bosses"]!.AsArray()[0]!.AsObject();
        Assert.IsType<JsonArray>(bossGroup["primeUnitSetIds"]);
        var recommendation = bossGroup["recommendations"]!.AsArray()[0]!.AsObject();
        Assert.False(string.IsNullOrWhiteSpace(recommendation["id"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(recommendation["kind"]!.GetValue<string>()));
        Assert.True(recommendation["efficiency"]!.GetValue<double>() > 0);
        Assert.IsType<JsonArray>(recommendation["mowReplacementIds"]);

        var heroSlots = recommendation["heroSlots"]!.AsArray();
        Assert.Equal(5, heroSlots.Count);
        Assert.All(heroSlots, slot =>
        {
            var slotObject = slot!.AsObject();
            Assert.False(string.IsNullOrWhiteSpace(slotObject["heroId"]!.GetValue<string>()));
            Assert.False(string.IsNullOrWhiteSpace(slotObject["roleId"]!.GetValue<string>()));
            Assert.IsType<bool>(slotObject["essential"]!.GetValue<bool>());
            Assert.IsType<JsonArray>(slotObject["replacementCharacterIds"]);
            Assert.Null(slotObject["name"]);
            Assert.Null(slotObject["label"]);
        });

        // A boss may now carry more than the historical fixed meta/alternate pair.
        Assert.Contains(
            data["bosses"]!.AsArray(),
            boss => boss!["recommendations"]!.AsArray().Count > 2);

        // primes[] mirrors the boss recommendation shape.
        var primeRecommendation = data["primes"]!.AsArray()[0]!["recommendations"]!.AsArray()[0]!.AsObject();
        Assert.False(string.IsNullOrWhiteSpace(primeRecommendation["id"]!.GetValue<string>()));
        Assert.Equal(5, primeRecommendation["heroSlots"]!.AsArray().Count);
    }

    /// <summary>
    /// The <c>events-calendar</c> dataset is projected relative to the load-time "now" (see
    /// add-game-events-calendar-dataset/design.md), so its hash — and the aggregate <c>sourceHash</c> that
    /// includes it — legitimately differs on every process start. Every other dataset's hash stays
    /// deterministic and is still snapshot-verified as-is.
    /// </summary>
    private static string ScrubTimeDependentHashes(string manifestJson)
    {
        var manifest = JsonNode.Parse(manifestJson)!.AsObject();
        manifest["sourceHash"] = "{time-dependent}";

        foreach (var dataset in manifest["datasets"]!.AsArray().Where(dataset => dataset!["key"]!.GetValue<string>() == "events-calendar"))
        {
            dataset!["hash"] = "{time-dependent}";
        }

        return manifest.ToJsonString();
    }
}
