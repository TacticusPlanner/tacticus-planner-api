using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TacticusPlanner.Catalog;
using Xunit;

namespace TacticusPlanner.Api.Tests;

public sealed class CatalogApiSmokeTests : IClassFixture<CatalogApiFactory>
{
    private readonly CatalogApiFactory factory;

    public CatalogApiSmokeTests(CatalogApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void ApiHostResolvesCatalogProviderAndParsesEmbeddedData()
    {
        var snapshot = factory.Services.GetRequiredService<ICatalogProvider>().Current;

        Assert.NotEmpty(snapshot.SourceHash);
        Assert.NotEmpty(snapshot.Units);
        Assert.NotEmpty(snapshot.CampaignBattles);
    }

    [Fact]
    public async Task OpenApiDocumentContainsCatalogRoutes()
    {
        var client = factory.CreateClient();

        var openApi = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Contains("/api/v1/catalog/manifest", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/catalog/units", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/catalog/campaign-events", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/catalog/lres", openApi, StringComparison.Ordinal);
        Assert.Contains("\"304\"", openApi, StringComparison.Ordinal);
        Assert.DoesNotContain("\"rewardId\"", openApi, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogManifestReturnsEtagDatasetUrlsAndSupportsNotModified()
    {
        var client = factory.CreateClient();

        var first = await client.GetAsync("/api/v1/catalog/manifest", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.NotNull(first.Headers.ETag);

        var manifestJson = await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var manifest = JsonDocument.Parse(manifestJson);
        var datasets = manifest.RootElement.GetProperty("datasets").EnumerateArray().ToArray();
        var campaignEvents = datasets.Single(dataset =>
            string.Equals(dataset.GetProperty("key").GetString(), "campaign-events", StringComparison.Ordinal)
        );

        Assert.False(string.IsNullOrWhiteSpace(campaignEvents.GetProperty("hash").GetString()));
        Assert.Equal("/api/v1/catalog/campaign-events", campaignEvents.GetProperty("url").GetString());

        using var secondRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog/manifest");
        secondRequest.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(first.Headers.ETag!.Tag));

        var second = await client.SendAsync(secondRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Empty(await second.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CatalogDatasetReturnsFullChunkWithoutEtagContract()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/catalog/campaign-events", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.ETag);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("campaign-events", document.RootElement.GetProperty("datasetKey").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("datasetHash").GetString()));
        Assert.NotEmpty(document.RootElement.GetProperty("items").EnumerateArray());
    }
}

public sealed class CatalogApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:planner-db"] = "Host=localhost;Port=5432;Database=tacticus_planner_test;Username=postgres;Password=postgres",
                ["Authentication:Authority"] = "https://example.ciamlogin.com/example.onmicrosoft.com/v2.0",
                ["Authentication:Audience"] = "api://tacticus-planner-api-test",
                ["TacticusApi:BaseUrl"] = "https://api.tacticusgame.com",
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { }
                );
        });
    }
}

public sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";

    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    )
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim("sub", "test-user"),
            new Claim("scp", "access_as_user"),
            new Claim("name", "Test User"),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
