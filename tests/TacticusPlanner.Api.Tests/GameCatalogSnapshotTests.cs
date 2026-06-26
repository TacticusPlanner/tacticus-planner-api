using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Xunit;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// Snapshot test guarding the public game catalog manifest: it makes a real (anonymous) call to
/// <c>/api/v1/game-catalog/manifest</c> and compares the full response — release metadata, source hash and
/// every dataset's key/hash/url — against a committed baseline. Any change to the served dataset shape or
/// content shifts a hash and trips this test. Regenerate the baseline with <c>UPDATE_SNAPSHOTS=1</c>.
/// </summary>
public sealed class GameCatalogSnapshotTests : IClassFixture<GameCatalogApiFactory>
{
    private readonly GameCatalogApiFactory factory;

    public GameCatalogSnapshotTests(GameCatalogApiFactory factory)
    {
        this.factory = factory;
    }

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
        using var manifest = JsonDocument.Parse(json);

        AssertMatchesSnapshot("game-catalog-manifest.json", JsonSerializer.Serialize(manifest, Indented));
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static void AssertMatchesSnapshot(string fileName, string actual, [CallerFilePath] string? callerPath = null)
    {
        var normalized = actual.Replace("\r\n", "\n");

        // Read the committed baseline from the test output directory (copied via the csproj), which is always
        // available — unlike [CallerFilePath], which deterministic CI builds rewrite to an unwritable path.
        var outputPath = Path.Combine(AppContext.BaseDirectory, "__snapshots__", fileName);

        if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") is "1" or "true")
        {
            // Update mode is a local-dev affordance: write the new baseline back to the source tree (and the
            // output copy) so it can be committed. CI never runs this branch.
            var sourcePath = Path.Combine(Path.GetDirectoryName(callerPath)!, "__snapshots__", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(sourcePath, normalized, new UTF8Encoding(false));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, normalized, new UTF8Encoding(false));
        }

        var expected = File.ReadAllText(outputPath).Replace("\r\n", "\n");
        Assert.Equal(expected, normalized);
    }
}
