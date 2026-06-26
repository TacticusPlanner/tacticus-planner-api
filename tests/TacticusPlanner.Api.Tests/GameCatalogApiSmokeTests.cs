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
using TacticusPlanner.GameCatalog;
using Xunit;

namespace TacticusPlanner.Api.Tests;

public sealed class GameCatalogApiSmokeTests : IClassFixture<GameCatalogApiFactory>
{
    private readonly GameCatalogApiFactory factory;

    public GameCatalogApiSmokeTests(GameCatalogApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void ApiHostResolvesGameCatalogProviderAndParsesEmbeddedData()
    {
        var snapshot = factory.Services.GetRequiredService<IGameCatalogProvider>().Current;

        Assert.NotEmpty(snapshot.SourceHash);
        Assert.NotEmpty(snapshot.Characters);
        Assert.NotEmpty(snapshot.Npcs);
        Assert.NotEmpty(snapshot.CampaignBattles);
    }

    [Fact]
    public async Task OpenApiDocumentContainsGameCatalogRoutes()
    {
        var client = factory.CreateClient();

        var openApi = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Contains("/api/v1/game-catalog/manifest", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/game-catalog/characters", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/game-catalog/npcs", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/game-catalog/mows", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/game-catalog/mow-upgrade-costs", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/game-catalog/upgrades", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/game-catalog/equipment", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/game-catalog/campaign-battles", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/game-catalog/campaign-definitions", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/game-catalog/lres", openApi, StringComparison.Ordinal);
        Assert.Contains("\"304\"", openApi, StringComparison.Ordinal);
        // The old parameterized chunk routes are gone.
        Assert.DoesNotContain("/api/v1/game-catalog/units/{factionId}", openApi, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/v1/game-catalog/upgrades/{rarity}", openApi, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GameCatalogManifestReturnsEtagDatasetUrlsAndSupportsNotModified()
    {
        var client = factory.CreateClient();

        var first = await client.GetAsync("/api/v1/game-catalog/manifest", TestContext.Current.CancellationToken);

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
        Assert.Equal("/api/v1/game-catalog/characters", characters.GetProperty("url").GetString());

        using var secondRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/game-catalog/manifest");
        secondRequest.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(first.Headers.ETag!.Tag));

        var second = await client.SendAsync(secondRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Empty(await second.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ServedDatasetReturnsEnvelopeWithoutEtagContract()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/game-catalog/upgrades", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.ETag);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("upgrades", document.RootElement.GetProperty("datasetKey").GetString());
        Assert.Equal("1.40", document.RootElement.GetProperty("gameVersion").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("datasetHash").GetString()));

        var upgrades = document.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.NotEmpty(upgrades);
        // An upgrade recipe nests a craftable ingredient's own recipe (no separate expansion property).
        Assert.DoesNotContain(upgrades, upgrade => upgrade.TryGetProperty("expanded", out _));
        var nested = upgrades
            .SelectMany(upgrade => upgrade.GetProperty("recipe").EnumerateArray())
            .First(ingredient => ingredient.TryGetProperty("recipe", out var inner)
                && inner.ValueKind == JsonValueKind.Array
                && inner.EnumerateArray().Any());
        Assert.NotEmpty(nested.GetProperty("recipe").EnumerateArray());
    }

    [Fact]
    public async Task ServedEntityEndpointsReturnDenormalizedData()
    {
        var client = factory.CreateClient();

        var characters = await GetData(client, "/api/v1/game-catalog/characters");
        Assert.NotEmpty(characters.EnumerateArray());
        var character = characters.EnumerateArray().First();
        Assert.False(string.IsNullOrEmpty(character.GetProperty("faction").GetString()));
        Assert.False(string.IsNullOrEmpty(character.GetProperty("alliance").GetString()));

        var npcs = await GetData(client, "/api/v1/game-catalog/npcs");
        Assert.NotEmpty(npcs.EnumerateArray());

        // mows is now a plain array of items; the shared cost ladder is its own dataset.
        var mows = await GetData(client, "/api/v1/game-catalog/mows");
        Assert.NotEmpty(mows.EnumerateArray());

        // The mow upgrade-cost ladder is keyed by ability level, starting at level 2.
        var mowUpgradeCosts = await GetData(client, "/api/v1/game-catalog/mow-upgrade-costs");
        Assert.NotEmpty(mowUpgradeCosts.EnumerateArray());
        Assert.Equal(2, mowUpgradeCosts.EnumerateArray().First().GetProperty("level").GetInt32());

        // equipment is a plain array; each item carries its matched upgrade-cost ladder inlined.
        var equipment = await GetData(client, "/api/v1/game-catalog/equipment");
        Assert.NotEmpty(equipment.EnumerateArray());
        Assert.Contains(
            equipment.EnumerateArray(),
            item => item.GetProperty("upgradeLevels").EnumerateArray().Any());

        // campaign-battles is a flat array of battles, each carrying its campaignGroupId.
        var battles = await GetData(client, "/api/v1/game-catalog/campaign-battles");
        Assert.NotEmpty(battles.EnumerateArray());
        var firstBattle = battles.EnumerateArray().First();
        Assert.False(string.IsNullOrEmpty(firstBattle.GetProperty("campaignGroupId").GetString()));

        // campaign-definitions references battle ids that resolve in campaign-battles.
        var definitions = await GetData(client, "/api/v1/game-catalog/campaign-definitions");
        var battleIds = battles.EnumerateArray()
            .Select(battle => battle.GetProperty("id").GetString())
            .ToHashSet(StringComparer.Ordinal);
        var firstDefinition = definitions.EnumerateArray().First();
        Assert.NotEmpty(firstDefinition.GetProperty("battleIds").EnumerateArray());
        Assert.All(
            firstDefinition.GetProperty("battleIds").EnumerateArray(),
            id => Assert.Contains(id.GetString(), battleIds));

        // lres is keyed by the unit snowprint id (no separate unitSnowprintId field).
        var lres = await GetData(client, "/api/v1/game-catalog/lres");
        var lucius = lres.EnumerateArray().Single(lre =>
            string.Equals(lre.GetProperty("id").GetString(), "emperLucius", StringComparison.Ordinal));
        Assert.NotEmpty(lucius.GetProperty("alpha").GetProperty("availableUnitIds").EnumerateArray());
    }

    [Fact]
    public async Task ManifestListsServedDatasetUrls()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/game-catalog/manifest", TestContext.Current.CancellationToken);
        var manifestJson = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var manifest = JsonDocument.Parse(manifestJson);
        var datasets = manifest.RootElement.GetProperty("datasets").EnumerateArray().ToArray();

        var keys = datasets.Select(dataset => dataset.GetProperty("key").GetString()).ToArray();
        Assert.Equal(GameCatalogDatasets.Served.OrderBy(key => key, StringComparer.Ordinal), keys);

        var characters = datasets.Single(dataset =>
            string.Equals(dataset.GetProperty("key").GetString(), "characters", StringComparison.Ordinal));
        Assert.Equal("/api/v1/game-catalog/characters", characters.GetProperty("url").GetString());
    }

    [Fact]
    public async Task GameCatalogEndpointsAllowAnonymousAccess()
    {
        var client = factory.CreateClient();

        using var manifestRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/game-catalog/manifest");
        manifestRequest.Headers.Add(TestAuthenticationHandler.NoAuthHeader, "1");
        var manifest = await client.SendAsync(manifestRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);

        using var charactersRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/game-catalog/characters");
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

public sealed class GameCatalogApiFactory : WebApplicationFactory<Program>
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
