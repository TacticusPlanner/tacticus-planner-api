using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.TacticusIntegration;

namespace TacticusPlanner.Api.Tests;

public sealed class TacticusIntegrationEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task ValidKeySavesIntegrationAndReturnsMaskedValues()
    {
        var client = await CreateProvisionedClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/tacticus-integration",
            new UpdateTacticusIntegrationRequest(FakeTacticusApi.ValidKey, "player-42"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<UpdateTacticusIntegrationResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.True(body.TacticusApiKeyConfigured);
        Assert.True(body.TacticusUserIdConfigured);
        Assert.Equal(FakeTacticusApi.PlayerName, body.PlayerName);
        Assert.Equal(FakeTacticusApi.PowerLevel, body.PowerLevel);
        Assert.EndsWith("-key", body.TacticusApiKeyMasked);
        Assert.DoesNotContain(FakeTacticusApi.ValidKey, body.TacticusApiKeyMasked);
    }

    [Fact]
    public async Task InvalidKeyIsRejected()
    {
        var client = await CreateProvisionedClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/tacticus-integration",
            new UpdateTacticusIntegrationRequest("not-a-real-key", null),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnprovisionedAccountIsNotFound()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, NewSubject());

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/tacticus-integration",
            new UpdateTacticusIntegrationRequest(FakeTacticusApi.ValidKey, null),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UserIdCanBeUpdatedWithoutResubmittingTheApiKey()
    {
        var client = await CreateProvisionedClientAsync();

        await client.PutAsJsonAsync(
            "/api/v1/me/tacticus-integration",
            new UpdateTacticusIntegrationRequest(FakeTacticusApi.ValidKey, "original-user-id"),
            TestContext.Current.CancellationToken
        );

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/tacticus-integration",
            new UpdateTacticusIntegrationRequest(null, "updated-user-id"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<UpdateTacticusIntegrationResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.True(body.TacticusApiKeyConfigured);
        Assert.True(body.TacticusUserIdConfigured);
        Assert.Null(body.PlayerName);
        Assert.Null(body.PowerLevel);
        Assert.EndsWith("r-id", body.TacticusUserIdMasked);
    }

    [Fact]
    public async Task UserIdCanBeClearedIndependentlyOfTheApiKey()
    {
        var client = await CreateProvisionedClientAsync();

        await client.PutAsJsonAsync(
            "/api/v1/me/tacticus-integration",
            new UpdateTacticusIntegrationRequest(FakeTacticusApi.ValidKey, "some-user-id"),
            TestContext.Current.CancellationToken
        );

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/tacticus-integration",
            new UpdateTacticusIntegrationRequest(null, null, ClearTacticusUserId: true),
            TestContext.Current.CancellationToken
        );

        var body = await response.Content
            .ReadFromJsonAsync<UpdateTacticusIntegrationResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.False(body.TacticusUserIdConfigured);
        Assert.True(body.TacticusApiKeyConfigured);
        Assert.Null(body.TacticusUserIdMasked);
    }

    private async Task<HttpClient> CreateProvisionedClientAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, NewSubject());

        // /me auto-provisions the Account + Profile that the integration endpoint requires to exist.
        await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        return client;
    }

    private static string NewSubject() => $"tacticus-integration-{Guid.NewGuid()}";
}
