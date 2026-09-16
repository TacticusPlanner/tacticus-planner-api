using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using TacticusPlanner.Api.Features.Analytics;
using TacticusPlanner.Api.Features.CurrentUser;
using TacticusPlanner.Domain.Accounts;

namespace TacticusPlanner.Api.Tests;

public sealed class CurrentUserEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task FirstCallProvisionsAccountWithOnboardingIncomplete()
    {
        var client = CreateAuthenticatedClient(NewSubject());

        var response = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.ApplicationUserId);
        Assert.False(body.HasCompletedOnboarding);
        Assert.Null(body.TacticusApiKeyMasked);
        Assert.Null(body.TacticusUserIdMasked);
    }

    [Fact]
    public async Task RepeatedCallsAreIdempotent()
    {
        var client = CreateAuthenticatedClient(NewSubject());

        var first = await (await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<CurrentUserResponse>(TestContext.Current.CancellationToken);
        var second = await (await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<CurrentUserResponse>(TestContext.Current.CancellationToken);

        Assert.Equal(first!.ApplicationUserId, second!.ApplicationUserId);
    }

    [Fact]
    public async Task ResponseCarriesTheIndependentlyDerivedAnalyticsId()
    {
        var client = CreateAuthenticatedClient(NewSubject());

        var body = await (await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<CurrentUserResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        var deriver = new AnalyticsIdentityDeriver(Options.Create(new AnalyticsOptions
        {
            IdentityKey = Convert.ToBase64String(PlannerApiFactory.TestAnalyticsIdentityKey),
        }));
        var expected = deriver.Derive(
            AccountId.From(body.ApplicationUserId),
            AnalyticsIdentityDeriver.PostHogDestination
        );

        Assert.Equal(expected, body.AnalyticsId);
    }

    [Fact]
    public async Task FirstAccessReportsAccountRegisteredExactlyOnce()
    {
        var client = CreateAuthenticatedClient(NewSubject());

        var first = await (await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<CurrentUserResponse>(TestContext.Current.CancellationToken);
        await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        var events = RecordingProductAnalytics.GetEvents(first!.AnalyticsId);

        Assert.Equal(["account_registered"], events);
    }

    [Fact]
    public async Task UnauthenticatedRequestIsRejected()
    {
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Add(PlannerTestAuthenticationHandler.NoAuthHeader, "1");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient CreateAuthenticatedClient(string subject)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, subject);
        return client;
    }

    private static string NewSubject() => $"current-user-{Guid.NewGuid()}";
}
