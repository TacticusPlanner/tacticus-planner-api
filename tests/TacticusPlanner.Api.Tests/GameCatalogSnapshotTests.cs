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
