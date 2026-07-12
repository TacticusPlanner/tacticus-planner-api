using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.TacticusApi.Models.Guild;

namespace TacticusPlanner.Api.Tests;

public sealed class PurgeGuildEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task UnauthenticatedRequestIsRejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.NoAuthHeader, "1");

        var response = await client.DeleteAsync("/api/v1/guilds/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnprovisionedAccountIsNotFound()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, $"guild-purge-{Guid.NewGuid()}");

        var response = await client.DeleteAsync("/api/v1/guilds/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NoLinkedGuildIsForbidden()
    {
        var (client, _) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);

        var response = await client.DeleteAsync("/api/v1/guilds/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NonLeaderLinkedMemberIsForbidden()
    {
        var (leaderClient, leaderTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory, "guild-purge-leader");
        var (memberClient, memberTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory, "guild-purge-member");

        var token = $"guild-token-{Guid.NewGuid()}";
        FakeTacticusApi.ConfigureGuildResponse(
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
        await leaderClient.PostAsJsonAsync("/api/v1/guilds/register", new RegisterGuildRequest(token), TestContext.Current.CancellationToken);

        var response = await memberClient.DeleteAsync("/api/v1/guilds/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LeaderCanPurgeAndAllMembersAreRemoved()
    {
        var (leaderClient, leaderTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory, "guild-purge-leader2");
        var (memberClient, memberTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory, "guild-purge-member2");

        var token = $"guild-token-{Guid.NewGuid()}";
        FakeTacticusApi.ConfigureGuildResponse(
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
        await leaderClient.PostAsJsonAsync("/api/v1/guilds/register", new RegisterGuildRequest(token), TestContext.Current.CancellationToken);

        var purgeResponse = await leaderClient.DeleteAsync("/api/v1/guilds/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, purgeResponse.StatusCode);

        var leaderAfter = await (await leaderClient.GetAsync("/api/v1/guilds/me", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<MyGuildResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(GuildStateValues.Unregistered, leaderAfter!.State);

        var memberAfter = await (await memberClient.GetAsync("/api/v1/guilds/me", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<MyGuildResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(GuildStateValues.Unregistered, memberAfter!.State);
    }

    [Fact]
    public async Task AnyCurrentMemberCanReRegisterAfterAPurge()
    {
        var (leaderClient, leaderTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory, "guild-purge-leader3");
        var guildId = Guid.NewGuid();
        var token = $"guild-token-{Guid.NewGuid()}";

        FakeTacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(guildId, "TAG", "Guild", 1, (leaderTacticusUserId, GuildRole.LEADER, 10, null))
        );
        await leaderClient.PostAsJsonAsync("/api/v1/guilds/register", new RegisterGuildRequest(token), TestContext.Current.CancellationToken);
        await leaderClient.DeleteAsync("/api/v1/guilds/me", TestContext.Current.CancellationToken);

        var reRegisterResponse = await leaderClient.PostAsJsonAsync(
            "/api/v1/guilds/register",
            new RegisterGuildRequest(token),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, reRegisterResponse.StatusCode);
    }
}
