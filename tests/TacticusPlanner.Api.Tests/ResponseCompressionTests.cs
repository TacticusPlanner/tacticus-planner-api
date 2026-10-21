using System.IO.Compression;
using System.Net;

namespace TacticusPlanner.Api.Tests;

public sealed class ResponseCompressionTests(GameCatalogApiFactory factory) : IClassFixture<GameCatalogApiFactory>
{
    private const string Path = "/api/v1/game-catalog/campaign-battles";

    private Task<HttpResponseMessage> GetAsync(string? acceptEncoding)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Path);
        request.Headers.Add(TestAuthenticationHandler.NoAuthHeader, "1");
        if (acceptEncoding is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
        }

        return factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task BrotliIsPreferredAndDecodesToTheUncompressedJson()
    {
        var ct = TestContext.Current.CancellationToken;
        var plain = await GetAsync(null);
        var compressed = await GetAsync("br, gzip");

        Assert.Equal(HttpStatusCode.OK, compressed.StatusCode);
        Assert.Equal(["br"], compressed.Content.Headers.ContentEncoding);
        Assert.Contains("Accept-Encoding", compressed.Headers.Vary);
        await using var decoded = new BrotliStream(await compressed.Content.ReadAsStreamAsync(ct), CompressionMode.Decompress);
        using var reader = new StreamReader(decoded);
        Assert.Equal(await plain.Content.ReadAsStringAsync(ct), await reader.ReadToEndAsync(ct));
    }

    [Fact]
    public async Task GzipIsUsedWhenBrotliIsNotAccepted()
    {
        var response = await GetAsync("gzip");

        Assert.Equal(["gzip"], response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task ClientWithoutAcceptEncodingGetsAnUncompressedResponse()
    {
        var response = await GetAsync(null);

        Assert.Empty(response.Content.Headers.ContentEncoding);
    }
}
