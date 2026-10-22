using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Tests;

public sealed class GameCatalogDatasetCachingTests(GameCatalogApiFactory factory) : IClassFixture<GameCatalogApiFactory>
{
    private sealed class SwappableCatalog(GameCatalogSnapshot current) : IGameCatalogProvider
    {
        public GameCatalogSnapshot Current { get; set; } = current;
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? acceptEncoding = null, string? ifNoneMatch = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(TestAuthenticationHandler.NoAuthHeader, "1");
        if (acceptEncoding is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
        }

        if (ifNoneMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DatasetCarriesAStrongETagAndRevalidationHeaders()
    {
        var response = await GetAsync(factory.CreateClient(), "/api/v1/game-catalog/upgrades");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        Assert.False(response.Headers.ETag.IsWeak);
        var cacheControl = response.Headers.CacheControl!;
        Assert.True(cacheControl.Public);
        Assert.True(cacheControl.MustRevalidate);
        Assert.Equal(TimeSpan.Zero, cacheControl.MaxAge);
        Assert.Contains("Accept-Encoding", response.Headers.Vary);
    }

    [Fact]
    public async Task MatchingIfNoneMatchReturnsAnEmptyNotModified()
    {
        var client = factory.CreateClient();
        var first = await GetAsync(client, "/api/v1/game-catalog/upgrades");
        var etag = first.Headers.ETag!.ToString();

        var second = await GetAsync(client, "/api/v1/game-catalog/upgrades", ifNoneMatch: etag);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Equal(etag, second.Headers.ETag?.ToString());
        Assert.Empty(await second.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StaleIfNoneMatchReturnsTheFullEnvelope()
    {
        var response = await GetAsync(factory.CreateClient(), "/api/v1/game-catalog/upgrades", ifNoneMatch: "\"stale\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("characters")]
    [InlineData("upgrades")]
    public async Task EncodingVariantsHaveDistinctValidatorsAndEnvelopeMatchesTheManifest(string dataset)
    {
        var client = factory.CreateClient();
        var identity = await GetAsync(client, $"/api/v1/game-catalog/{dataset}");
        var brotli = await GetAsync(client, $"/api/v1/game-catalog/{dataset}", "br");
        var gzip = await GetAsync(client, $"/api/v1/game-catalog/{dataset}", "gzip");

        var validators = new[] { identity, brotli, gzip }.Select(response => response.Headers.ETag!.ToString()).ToList();
        Assert.Equal(3, validators.Distinct().Count());
        Assert.Contains("Accept-Encoding", brotli.Headers.Vary);
        // A validator is only honoured for its own representation.
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, $"/api/v1/game-catalog/{dataset}", "br", validators[2])).StatusCode);

        var manifest = JsonNode.Parse(await (await GetAsync(client, "/api/v1/game-catalog/manifest")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        var envelope = JsonNode.Parse(await identity.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        var manifestHash = manifest["datasets"]!.AsArray().Single(entry => (string?)entry!["key"] == dataset)!["hash"]!.ToString();
        Assert.Equal(manifestHash, envelope["datasetHash"]!.ToString());
    }

    [Fact]
    public async Task MetadataOnlyReleaseAndContentChangeInvalidateTheValidator()
    {
        var real = factory.Services.GetRequiredService<IGameCatalogProvider>().Current;
        var catalog = new SwappableCatalog(real);
        using var swapped = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IGameCatalogProvider>(catalog)));
        var client = swapped.CreateClient();
        var before = (await GetAsync(client, "/api/v1/game-catalog/upgrades")).Headers.ETag!.ToString();

        // Same payload hash, new release metadata.
        catalog.Current = real with { SourceHash = real.SourceHash + "x", Version = real.Version + "x" };
        var metadataOnly = await GetAsync(client, "/api/v1/game-catalog/upgrades", ifNoneMatch: before);
        Assert.Equal(HttpStatusCode.OK, metadataOnly.StatusCode);
        Assert.NotEqual(before, metadataOnly.Headers.ETag!.ToString());

        // Different content (and therefore dataset hash).
        catalog.Current = real with
        {
            UpgradeViews = real.UpgradeViews.Skip(1).ToList(),
            DatasetHashes = new Dictionary<string, string>(real.DatasetHashes) { [GameCatalogDatasets.Upgrades] = "changed" },
        };
        var changed = await GetAsync(client, "/api/v1/game-catalog/upgrades");
        Assert.NotEqual(before, changed.Headers.ETag!.ToString());
        Assert.NotEqual(metadataOnly.Headers.ETag.ToString(), changed.Headers.ETag.ToString());
    }

    private static async Task<string> ManifestUrlAsync(HttpClient client, string dataset)
    {
        var manifest = JsonNode.Parse(await (await GetAsync(client, "/api/v1/game-catalog/manifest")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        return manifest["datasets"]!.AsArray().Single(entry => (string?)entry!["key"] == dataset)!["url"]!.ToString();
    }

    [Fact]
    public async Task TheManifestUrlIsAnImmutableContentAddress()
    {
        var client = factory.CreateClient();
        var url = await ManifestUrlAsync(client, "upgrades");

        var hashed = await GetAsync(client, url);
        var bare = await GetAsync(client, "/api/v1/game-catalog/upgrades");

        Assert.Equal(HttpStatusCode.OK, hashed.StatusCode);
        var cacheControl = hashed.Headers.CacheControl!;
        Assert.True(cacheControl.Public);
        Assert.Equal(TimeSpan.FromSeconds(31536000), cacheControl.MaxAge);
        Assert.Contains("immutable", cacheControl.Extensions.Select(extension => extension.Name));
        Assert.False(cacheControl.MustRevalidate);
        Assert.Contains("Accept-Encoding", hashed.Headers.Vary);
        // Same representation, same strong validator, whichever route served it.
        Assert.Equal(bare.Headers.ETag, hashed.Headers.ETag);
        Assert.False(hashed.Headers.ETag!.IsWeak);
        Assert.StartsWith($"\"{url[(url.LastIndexOf('/') + 1)..]}", hashed.Headers.ETag.Tag);
    }

    [Fact]
    public async Task AHashThatIsNotTheCurrentEnvelopesIsNotFound()
    {
        var response = await GetAsync(factory.CreateClient(), $"/api/v1/game-catalog/upgrades/{new string('0', 64)}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AMetadataOnlyReleaseMovesTheUrlAndTheManifestValidator()
    {
        var real = factory.Services.GetRequiredService<IGameCatalogProvider>().Current;
        var catalog = new SwappableCatalog(real);
        using var swapped = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IGameCatalogProvider>(catalog)));
        var client = swapped.CreateClient();
        var before = await ManifestUrlAsync(client, "upgrades");
        var manifestEtag = (await GetAsync(client, "/api/v1/game-catalog/manifest")).Headers.ETag!.ToString();

        // Same payload hash, new release metadata.
        catalog.Current = real with { Version = real.Version + "x" };
        var after = await ManifestUrlAsync(client, "upgrades");
        var manifest = await GetAsync(client, "/api/v1/game-catalog/manifest", ifNoneMatch: manifestEtag);

        Assert.NotEqual(before, after);
        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, after)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(client, before)).StatusCode);
    }
}
