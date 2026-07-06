using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.V1Import;

namespace TacticusPlanner.Api.Tests;

public sealed class V1ImportEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task ValidCredentialsImportTacticusKeyAndUserId()
    {
        var client = await CreateProvisionedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            new ImportV1ProfileRequest(FakeTacticusV1Client.ValidUsername, FakeTacticusV1Client.ValidPassword),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<ImportV1ProfileResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(FakeTacticusApi.PlayerName, body.PlayerName);
        Assert.NotNull(body.TacticusApiKeyMasked);
        Assert.NotNull(body.TacticusUserIdMasked);
    }

    [Fact]
    public async Task InvalidV1CredentialsAreRejected()
    {
        var client = await CreateProvisionedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            new ImportV1ProfileRequest(FakeTacticusV1Client.ValidUsername, "wrong-password"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task V1ProfileWithoutATacticusApiKeyIsRejected()
    {
        var client = await CreateProvisionedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            new ImportV1ProfileRequest(FakeTacticusV1Client.UsernameWithoutTacticusKey, FakeTacticusV1Client.ValidPassword),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MissingCredentialsAreRejected()
    {
        var client = await CreateProvisionedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            new ImportV1ProfileRequest(null, null),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpClient> CreateProvisionedClientAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, NewSubject());

        await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        return client;
    }

    private static string NewSubject() => $"v1-import-{Guid.NewGuid()}";
}
