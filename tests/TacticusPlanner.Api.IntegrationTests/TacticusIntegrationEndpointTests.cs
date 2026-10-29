using System.Net;
using System.Text.Json;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.Api.Features.TacticusIntegration;
using TacticusPlanner.TacticusApi.Models.Guild;

namespace TacticusPlanner.Api.IntegrationTests;

public sealed class TacticusIntegrationEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task ValidKeySavesIntegrationAndReturnsMaskedValues()
    {
        var client = await CreateProvisionedClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/v1/account/tacticus-integration",
            new UpdateTacticusIntegrationRequest(FakeTacticusApi.ValidKey, Guid.NewGuid().ToString()),
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
            "/api/v1/account/tacticus-integration",
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
            "/api/v1/account/tacticus-integration",
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
            "/api/v1/account/tacticus-integration",
            new UpdateTacticusIntegrationRequest(FakeTacticusApi.ValidKey, Guid.NewGuid().ToString()),
            TestContext.Current.CancellationToken
        );

        var updatedUserId = Guid.NewGuid();
        var response = await client.PutAsJsonAsync(
            "/api/v1/account/tacticus-integration",
            new UpdateTacticusIntegrationRequest(null, $"  {updatedUserId.ToString().ToUpperInvariant()}  "),
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
        Assert.EndsWith(updatedUserId.ToString()[^4..], body.TacticusUserIdMasked); // stored canonical (trimmed, lower-case)
    }

    [Fact]
    public async Task UserIdCanBeClearedIndependentlyOfTheApiKey()
    {
        var client = await CreateProvisionedClientAsync();

        await client.PutAsJsonAsync(
            "/api/v1/account/tacticus-integration",
            new UpdateTacticusIntegrationRequest(FakeTacticusApi.ValidKey, Guid.NewGuid().ToString()),
            TestContext.Current.CancellationToken
        );

        var response = await client.PutAsJsonAsync(
            "/api/v1/account/tacticus-integration",
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

    [Fact]
    public async Task NonGuidUserIdIsRejected()
    {
        var client = await CreateProvisionedClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/v1/account/tacticus-integration",
            new UpdateTacticusIntegrationRequest(FakeTacticusApi.ValidKey, "player-42"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UserIdAlreadyClaimedByAnotherProfileIsAConflict()
    {
        var userId = Guid.NewGuid();
        var first = await CreateProvisionedClientAsync();
        (await PutUserIdAsync(first, userId.ToString())).EnsureSuccessStatusCode();
        var second = await CreateProvisionedClientAsync();

        // A differently formatted spelling of the same GUID is the same id.
        var response = await PutUserIdAsync(second, userId.ToString("B").ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("tacticusUserIdInUse", problem.GetProperty("issueCode").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClearingOrChangingTheUserIdUnlinksTheGuildMember(bool clear)
    {
        var ct = TestContext.Current.CancellationToken;
        var (leaderClient, leaderTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var (memberClient, memberTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var token = $"guild-token-{Guid.NewGuid()}";
        factory.TacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(
                Guid.NewGuid(), $"U{Guid.NewGuid():N}"[..10], "Guild", 1,
                (leaderTacticusUserId, GuildRole.LEADER, 10, null),
                (memberTacticusUserId, GuildRole.CO_LEADER, 5, null)));
        (await leaderClient.PostAsJsonAsync("/api/v1/guild/register", new RegisterGuildRequest(token), ct)).EnsureSuccessStatusCode();
        Assert.True((await MemberAsync(leaderClient, memberTacticusUserId)).IsLinked);

        var response = clear
            ? await memberClient.PutAsJsonAsync(
                "/api/v1/account/tacticus-integration", new UpdateTacticusIntegrationRequest(null, null, ClearTacticusUserId: true), ct)
            : await PutUserIdAsync(memberClient, Guid.NewGuid().ToString());

        response.EnsureSuccessStatusCode();
        var member = await MemberAsync(leaderClient, memberTacticusUserId);
        Assert.False(member.IsLinked);
        Assert.Null(member.LinkedPlayerName);
        Assert.Null(member.LastActiveInPlannerOn);
        var memberView = await (await memberClient.GetAsync("/api/v1/guild", ct)).Content.ReadFromJsonAsync<MyGuildResponse>(ct);
        Assert.NotEqual(MyGuildState.Registered, memberView!.State);
    }

    [Fact]
    public async Task ResubmittingTheSameUserIdKeepsTheGuildLink()
    {
        var ct = TestContext.Current.CancellationToken;
        var (leaderClient, leaderTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var token = $"guild-token-{Guid.NewGuid()}";
        factory.TacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(
                Guid.NewGuid(), $"K{Guid.NewGuid():N}"[..10], "Guild", 1, (leaderTacticusUserId, GuildRole.LEADER, 10, null)));
        (await leaderClient.PostAsJsonAsync("/api/v1/guild/register", new RegisterGuildRequest(token), ct)).EnsureSuccessStatusCode();

        (await PutUserIdAsync(leaderClient, leaderTacticusUserId.ToString())).EnsureSuccessStatusCode();

        Assert.True((await MemberAsync(leaderClient, leaderTacticusUserId)).IsLinked);
    }

    private static Task<HttpResponseMessage> PutUserIdAsync(HttpClient client, string userId) =>
        client.PutAsJsonAsync(
            "/api/v1/account/tacticus-integration",
            new UpdateTacticusIntegrationRequest(FakeTacticusApi.ValidKey, userId),
            TestContext.Current.CancellationToken);

    private static async Task<GuildMemberSummary> MemberAsync(HttpClient leaderClient, Guid tacticusUserId)
    {
        var guild = await (await leaderClient.GetAsync("/api/v1/guild", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<MyGuildResponse>(TestContext.Current.CancellationToken);
        return Assert.Single(guild!.Guild!.Members, member => member.MaskedTacticusUserId.EndsWith(tacticusUserId.ToString()[^4..], StringComparison.Ordinal));
    }

    private async Task<HttpClient> CreateProvisionedClientAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, NewSubject());

        // GET /account auto-provisions the Account + Profile that the integration endpoint requires to exist.
        await client.GetAsync("/api/v1/account", TestContext.Current.CancellationToken);

        return client;
    }

    private static string NewSubject() => $"tacticus-integration-{Guid.NewGuid()}";
}
