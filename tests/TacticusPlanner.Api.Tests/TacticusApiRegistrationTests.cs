using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.TacticusApi;

namespace TacticusPlanner.Api.Tests;

public class TacticusApiRegistrationTests
{
    // Refit 16 has no reflection request builder by default; a non-generated registration only fails when the
    // client is first resolved, so this guards against a runtime-only 500.
    [Fact]
    public void TacticusApiClientResolvesWithoutReflectionBuilder()
    {
        using var provider = new ServiceCollection().AddTacticusApi("https://example.test").BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ITacticusApi>());
    }
}
