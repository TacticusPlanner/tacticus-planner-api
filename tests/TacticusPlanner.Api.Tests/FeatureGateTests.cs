using System.Net;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;
using TacticusPlanner.Api.Features.FeatureFlags;

namespace TacticusPlanner.Api.Tests;

public sealed class FeatureGateTests(GameCatalogApiFactory factory) : IClassFixture<GameCatalogApiFactory>
{
    [Fact]
    public async Task AGatedEndpointIsNotFoundWhileItsFlagIsOff()
    {
        var response = await GetProbeAsync(factory);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AGatedEndpointRunsWhileItsFlagIsOn()
    {
        using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting($"FeatureManagement:{FeatureGateProbeEndpoint.Flag}", "true"));

        var response = await GetProbeAsync(host);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("reached", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FlagsFromBothConfigurationSchemasAreRead()
    {
        // App Configuration emits the Microsoft schema (feature_management); appsettings use the .NET schema.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureManagement:FromAppSettings"] = "true",
                ["FeatureManagement:OffInAppSettings"] = "false",
                ["feature_management:feature_flags:0:id"] = "FromAppConfiguration",
                ["feature_management:feature_flags:0:enabled"] = "true",
                ["feature_management:feature_flags:1:id"] = "OffInAppConfiguration",
                ["feature_management:feature_flags:1:enabled"] = "false",
            })
            .Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(configuration);
        services.AddFeatureManagement();
        await using var provider = services.BuildServiceProvider();
        var featureManager = provider.GetRequiredService<IFeatureManager>();

        var names = new List<string>();
        await foreach (var name in featureManager.GetFeatureNamesAsync())
        {
            names.Add(name);
        }

        names.Sort(StringComparer.Ordinal);
        Assert.Equal(["FromAppConfiguration", "FromAppSettings", "OffInAppConfiguration", "OffInAppSettings"], names);
        Assert.True(await featureManager.IsEnabledAsync("FromAppSettings"));
        Assert.True(await featureManager.IsEnabledAsync("FromAppConfiguration"));
        Assert.False(await featureManager.IsEnabledAsync("OffInAppSettings"));
        Assert.False(await featureManager.IsEnabledAsync("OffInAppConfiguration"));
    }

    private static async Task<HttpResponseMessage> GetProbeAsync(WebApplicationFactory<Program> host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/feature-gate-probe");
        request.Headers.Add(TestAuthenticationHandler.NoAuthHeader, "1");
        return await host.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }
}

/// <summary>Test-only endpoint behind a flag. Calls <c>Options</c> after the gate, as rate-limited endpoints do.</summary>
public sealed class FeatureGateProbeEndpoint : EndpointWithoutRequest<string>
{
    public const string Flag = "FeatureGateProbe";

    public override void Configure()
    {
        Get("test/feature-gate-probe");
        AllowAnonymous();
        this.RequireFeature(Flag);
        Options(builder => builder.ExcludeFromDescription());
    }

    public override Task HandleAsync(CancellationToken ct) => Send.StringAsync("reached", cancellation: ct);
}
