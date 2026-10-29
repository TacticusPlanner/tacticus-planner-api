using System.Net;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.Api.Features.TacticusIntegration;
using TacticusPlanner.TacticusApi.Models.Guild;

namespace TacticusPlanner.Api.IntegrationTests;

public sealed class PurgeGuildEndpointTests : IAsyncLifetime
{
    private readonly PlannerApiFactory factory = new();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task UnauthenticatedRequestIsRejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.NoAuthHeader, "1");

        var response = await client.DeleteAsync("/api/v1/guild", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnprovisionedAccountIsNotFound()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, $"guild-purge-{Guid.NewGuid()}");

        var response = await client.DeleteAsync("/api/v1/guild", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NoLinkedGuildIsForbidden()
    {
        var (client, _) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);

        var response = await client.DeleteAsync("/api/v1/guild", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NonLeaderLinkedMemberIsForbidden()
    {
        var (leaderClient, leaderTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory, "guild-purge-leader");
        var (memberClient, memberTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory, "guild-purge-member");

        var token = $"guild-token-{Guid.NewGuid()}";
        factory.TacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(
                Guid.NewGuid(),
                "TAG",
                "Guild",
                1,
                (leaderTacticusUserId, GuildRole.LEADER, 10, null),
                (memberTacticusUserId, GuildRole.MEMBER, 5, null)
            )
        );
        await leaderClient.PostAsJsonAsync("/api/v1/guild/register", new RegisterGuildRequest(token), TestContext.Current.CancellationToken);

        var response = await memberClient.DeleteAsync("/api/v1/guild", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LeaderCanPurgeAndAllMembersAreRemoved()
    {
        var (leaderClient, leaderTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory, "guild-purge-leader2");
        var (memberClient, memberTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory, "guild-purge-member2");

        var token = $"guild-token-{Guid.NewGuid()}";
        factory.TacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(
                Guid.NewGuid(),
                "TAG",
                "Guild",
                1,
                (leaderTacticusUserId, GuildRole.LEADER, 10, null),
                (memberTacticusUserId, GuildRole.MEMBER, 5, null)
            )
        );
        await leaderClient.PostAsJsonAsync("/api/v1/guild/register", new RegisterGuildRequest(token), TestContext.Current.CancellationToken);

        var purgeResponse = await leaderClient.DeleteAsync("/api/v1/guild", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, purgeResponse.StatusCode);

        var leaderAfter = await (await leaderClient.GetAsync("/api/v1/guild", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<MyGuildResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(MyGuildState.Unregistered, leaderAfter!.State);

        var memberAfter = await (await memberClient.GetAsync("/api/v1/guild", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<MyGuildResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(MyGuildState.Unregistered, memberAfter!.State);
    }

    [Fact]
    public async Task AnyCurrentMemberCanReRegisterAfterAPurge()
    {
        var (leaderClient, leaderTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory, "guild-purge-leader3");
        var guildId = Guid.NewGuid();
        var token = $"guild-token-{Guid.NewGuid()}";

        factory.TacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(guildId, "TAG", "Guild", 1, (leaderTacticusUserId, GuildRole.LEADER, 10, null))
        );
        await leaderClient.PostAsJsonAsync("/api/v1/guild/register", new RegisterGuildRequest(token), TestContext.Current.CancellationToken);
        await leaderClient.DeleteAsync("/api/v1/guild", TestContext.Current.CancellationToken);

        var reRegisterResponse = await leaderClient.PostAsJsonAsync(
            "/api/v1/guild/register",
            new RegisterGuildRequest(token),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, reRegisterResponse.StatusCode);
    }

    [Fact]
    public async Task CoLeaderWhoLeftUpstreamIsForbiddenAndTheGuildIsKept()
    {
        var (leaderClient, coLeaderClient, token, guildId, leaderTacticusUserId, _) = await RegisterWithCoLeaderAsync();

        // The Co-Leader leaves the guild in Tacticus; no Planner sync has run since.
        factory.TacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(guildId, "TAG", "Guild", 1, (leaderTacticusUserId, GuildRole.LEADER, 10, null))
        );

        var response = await coLeaderClient.DeleteAsync("/api/v1/guild", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertStillRegisteredAsync(leaderClient);
    }

    [Fact]
    public async Task CoLeaderWithAClearedTacticusUserIdIsForbidden()
    {
        var (leaderClient, coLeaderClient, _, _, _, _) = await RegisterWithCoLeaderAsync();
        (await coLeaderClient.PutAsJsonAsync(
            "/api/v1/account/tacticus-integration",
            new UpdateTacticusIntegrationRequest(null, null, ClearTacticusUserId: true),
            TestContext.Current.CancellationToken
        )).EnsureSuccessStatusCode();

        var response = await coLeaderClient.DeleteAsync("/api/v1/guild", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertStillRegisteredAsync(leaderClient);
    }

    [Fact]
    public async Task CurrentCoLeaderCanPurge()
    {
        var (leaderClient, coLeaderClient, _, _, _, _) = await RegisterWithCoLeaderAsync();

        var response = await coLeaderClient.DeleteAsync("/api/v1/guild", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = await (await leaderClient.GetAsync("/api/v1/guild", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<MyGuildResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(MyGuildState.Unregistered, after!.State);
    }

    [Fact]
    public async Task UpstreamOutageFailsClosedAndTheGuildIsKept()
    {
        var (leaderClient, _, token, _, _, _) = await RegisterWithCoLeaderAsync();
        factory.TacticusApi.ConfigureGuildUnavailable(token);

        var response = await leaderClient.DeleteAsync("/api/v1/guild", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertStillRegisteredAsync(leaderClient);
    }

    private async Task<(HttpClient Leader, HttpClient CoLeader, string Token, Guid GuildId, Guid LeaderId, Guid CoLeaderId)>
        RegisterWithCoLeaderAsync()
    {
        var (leaderClient, leaderTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var (coLeaderClient, coLeaderTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var token = $"guild-token-{Guid.NewGuid()}";
        var guildId = Guid.NewGuid();
        factory.TacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(
                guildId,
                "TAG",
                "Guild",
                1,
                (leaderTacticusUserId, GuildRole.LEADER, 10, null),
                (coLeaderTacticusUserId, GuildRole.CO_LEADER, 5, null)
            )
        );
        (await leaderClient.PostAsJsonAsync("/api/v1/guild/register", new RegisterGuildRequest(token), TestContext.Current.CancellationToken))
            .EnsureSuccessStatusCode();
        return (leaderClient, coLeaderClient, token, guildId, leaderTacticusUserId, coLeaderTacticusUserId);
    }

    private static async Task AssertStillRegisteredAsync(HttpClient client)
    {
        var guild = await (await client.GetAsync("/api/v1/guild", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<MyGuildResponse>(TestContext.Current.CancellationToken);
        Assert.NotEqual(MyGuildState.Unregistered, guild!.State);
    }
}
