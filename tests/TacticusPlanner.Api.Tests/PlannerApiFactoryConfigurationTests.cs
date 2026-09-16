using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// Guards the test host's own configuration, not endpoint behavior - <see cref="PlannerApiFactory"/>
/// must never let a real secret from the committed appsettings files reach the test process. Regression
/// coverage for a real incident: <c>appsettings.Development.json</c> started shipping a real PostHog
/// project token for local `dotnet run`, and without an explicit override here the test host picked it
/// up too, registering the live PostHog SDK client during test runs.
/// </summary>
public sealed class PlannerApiFactoryConfigurationTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public void AnalyticsProjectTokenIsClearedRegardlessOfCommittedAppsettings()
    {
        var configuration = factory.Services.GetRequiredService<IConfiguration>();

        Assert.True(string.IsNullOrEmpty(configuration["Analytics:ProjectToken"]));
    }
}
