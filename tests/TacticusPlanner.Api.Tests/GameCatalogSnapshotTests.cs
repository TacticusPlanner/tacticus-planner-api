using System.Net;
using Xunit;
using static VerifyXunit.Verifier;

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

        await VerifyJson(json);
    }
}
