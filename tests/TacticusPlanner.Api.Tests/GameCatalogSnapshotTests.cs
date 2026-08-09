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
