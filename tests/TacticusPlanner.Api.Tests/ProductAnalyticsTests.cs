using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Analytics;

namespace TacticusPlanner.Api.Tests;

public sealed class ProductAnalyticsTests
{
    [Fact]
    public void NoProjectTokenSelectsTheInertImplementation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Analytics:IdentityKey"] = Convert.ToBase64String(new byte[32]),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAnalyticsFeature(configuration, validateOnStart: false);
        using var provider = services.BuildServiceProvider();

        var analytics = provider.GetRequiredService<IProductAnalytics>();

        Assert.IsType<NullProductAnalytics>(analytics);
        // Performs an event-emitting operation; no outbound request is possible since NullProductAnalytics
        // holds no HTTP client at all (specs/product-analytics-events/spec.md's "inert when not configured").
        analytics.AccountRegistered("some-analytics-id");
        analytics.GuildRegistered("some-analytics-id");
    }

    [Fact]
    public void EveryDeclaredEventMethodTakesOnlyTheAnalyticsId()
    {
        // Structural check for design.md's "typed event methods, not a free-form property bag": there is no
        // parameter through which a caller could attach an account id, display name, or other free-text
        // property, so the privacy floor in specs/product-analytics-events/spec.md is a compile-time
        // property of IProductAnalytics rather than a convention call sites must remember to follow.
        var methods = typeof(IProductAnalytics).GetMethods();

        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
        {
            var parameters = method.GetParameters();
            Assert.Single(parameters);
            Assert.Equal(typeof(string), parameters[0].ParameterType);
            Assert.Equal("analyticsId", parameters[0].Name);
        });
    }
}
