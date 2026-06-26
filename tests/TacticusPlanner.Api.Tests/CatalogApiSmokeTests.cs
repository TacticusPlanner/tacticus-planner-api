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
        Assert.NotEmpty(snapshot.Characters);
        Assert.NotEmpty(snapshot.Npcs);
        Assert.NotEmpty(snapshot.CampaignBattles);
    }

    [Fact]
    public async Task OpenApiDocumentContainsCatalogRoutes()
    {
        var client = factory.CreateClient();

        var openApi = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Contains("/api/v1/catalog/manifest", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/catalog/characters", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/catalog/npcs", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/catalog/mows", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/catalog/upgrades", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/catalog/equipment", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/catalog/campaign-battles", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/catalog/lres", openApi, StringComparison.Ordinal);
        Assert.Contains("\"304\"", openApi, StringComparison.Ordinal);
        // The old parameterized chunk routes are gone.
        Assert.DoesNotContain("/api/v1/catalog/units/{factionId}", openApi, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/v1/catalog/upgrades/{rarity}", openApi, StringComparison.Ordinal);
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
        Assert.Equal("1.40", manifest.RootElement.GetProperty("gameVersion").GetString());
        var datasets = manifest.RootElement.GetProperty("datasets").EnumerateArray().ToArray();
        var characters = datasets.Single(dataset =>
            string.Equals(dataset.GetProperty("key").GetString(), "characters", StringComparison.Ordinal)
        );

        Assert.False(string.IsNullOrWhiteSpace(characters.GetProperty("hash").GetString()));
        Assert.Equal("/api/v1/catalog/characters", characters.GetProperty("url").GetString());

        using var secondRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog/manifest");
        secondRequest.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(first.Headers.ETag!.Tag));

        var second = await client.SendAsync(secondRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Empty(await second.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ServedDatasetReturnsEnvelopeWithoutEtagContract()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/catalog/upgrades", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.ETag);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("upgrades", document.RootElement.GetProperty("datasetKey").GetString());
        Assert.Equal("1.40", document.RootElement.GetProperty("gameVersion").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("datasetHash").GetString()));

        var upgrades = document.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.NotEmpty(upgrades);
        // A crafted upgrade exposes its expanded recipe split into base + crafted totals.
        var crafted = upgrades.First(upgrade => upgrade.GetProperty("craftable").GetBoolean());
        var expanded = crafted.GetProperty("expanded");
        Assert.True(expanded.GetProperty("totalBaseCount").GetInt32() > 0);
    }

    [Fact]
    public async Task ServedEntityEndpointsReturnDenormalizedData()
    {
        var client = factory.CreateClient();

        var characters = await GetData(client, "/api/v1/catalog/characters");
        Assert.NotEmpty(characters.EnumerateArray());
        var character = characters.EnumerateArray().First();
        Assert.False(string.IsNullOrEmpty(character.GetProperty("faction").GetString()));
        Assert.False(string.IsNullOrEmpty(character.GetProperty("alliance").GetString()));

        var npcs = await GetData(client, "/api/v1/catalog/npcs");
        Assert.NotEmpty(npcs.EnumerateArray());

        var mows = await GetData(client, "/api/v1/catalog/mows");
        Assert.NotEmpty(mows.GetProperty("items").EnumerateArray());
        Assert.NotEmpty(mows.GetProperty("upgradeCosts").EnumerateArray());

        var equipment = await GetData(client, "/api/v1/catalog/equipment");
        Assert.NotEmpty(equipment.GetProperty("items").EnumerateArray());
        Assert.NotEmpty(equipment.GetProperty("upgradeCostsByRarity").EnumerateArray());

        var groups = await GetData(client, "/api/v1/catalog/campaign-battles");
        var firstGroup = groups.EnumerateArray().First();
        Assert.NotEmpty(firstGroup.GetProperty("battles").EnumerateArray());

        var lres = await GetData(client, "/api/v1/catalog/lres");
        var lucius = lres.EnumerateArray().Single(lre =>
            string.Equals(lre.GetProperty("unitSnowprintId").GetString(), "emperLucius", StringComparison.Ordinal));
        Assert.NotEmpty(lucius.GetProperty("alpha").GetProperty("availableUnitIds").EnumerateArray());
    }

    [Fact]
    public async Task ManifestListsServedDatasetUrls()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/catalog/manifest", TestContext.Current.CancellationToken);
        var manifestJson = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var manifest = JsonDocument.Parse(manifestJson);
        var datasets = manifest.RootElement.GetProperty("datasets").EnumerateArray().ToArray();

        var keys = datasets.Select(dataset => dataset.GetProperty("key").GetString()).ToArray();
        Assert.Equal(CatalogDatasets.Served.OrderBy(key => key, StringComparer.Ordinal), keys);

        var characters = datasets.Single(dataset =>
            string.Equals(dataset.GetProperty("key").GetString(), "characters", StringComparison.Ordinal));
        Assert.Equal("/api/v1/catalog/characters", characters.GetProperty("url").GetString());
    }

    [Fact]
    public async Task CatalogEndpointsAllowAnonymousAccess()
    {
        var client = factory.CreateClient();

        using var manifestRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog/manifest");
        manifestRequest.Headers.Add(TestAuthenticationHandler.NoAuthHeader, "1");
        var manifest = await client.SendAsync(manifestRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);

        using var charactersRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog/characters");
        charactersRequest.Headers.Add(TestAuthenticationHandler.NoAuthHeader, "1");
        var characters = await client.SendAsync(charactersRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, characters.StatusCode);
    }

    private static async Task<JsonElement> GetData(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data").Clone();
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

    // Lets a test opt out of the always-on test auth to exercise anonymous (catalog) access.
    public const string NoAuthHeader = "X-Test-NoAuth";

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
        if (Request.Headers.ContainsKey(NoAuthHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

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
