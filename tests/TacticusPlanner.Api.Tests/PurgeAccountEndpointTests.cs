using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.CurrentUser;

namespace TacticusPlanner.Api.Tests;

public sealed class PurgeAccountEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task PurgeDeletesTheAccountAndSubsequentMeReprovisionsAFreshOne()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, NewSubject());

        var before = await (await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<CurrentUserResponse>(TestContext.Current.CancellationToken);

        var purgeResponse = await client.DeleteAsync("/api/v1/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, purgeResponse.StatusCode);

        var after = await (await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<CurrentUserResponse>(TestContext.Current.CancellationToken);

        Assert.NotEqual(before!.ApplicationUserId, after!.ApplicationUserId);
        Assert.False(after.HasCompletedOnboarding);
    }

    [Fact]
    public async Task PurgeIsIdempotentForAnAccountThatWasNeverProvisioned()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, NewSubject());

        var response = await client.DeleteAsync("/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static string NewSubject() => $"purge-{Guid.NewGuid()}";
}
