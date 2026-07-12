using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.TacticusApi.Models.Guild;

namespace TacticusPlanner.Api.Tests;

public sealed class GetMyGuildEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task UnauthenticatedRequestIsRejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.NoAuthHeader, "1");

        var response = await client.GetAsync("/api/v1/guilds/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnprovisionedAccountIsNotFound()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, $"guild-me-{Guid.NewGuid()}");

        var response = await client.GetAsync("/api/v1/guilds/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NoTacticusUserIdConfiguredReturnsTacticusUserIdRequiredState()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, $"guild-me-{Guid.NewGuid()}");
        await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        var response = await client.GetAsync("/api/v1/guilds/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MyGuildResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(GuildStateValues.TacticusUserIdRequired, body.State);
        Assert.Null(body.Guild);
    }

    [Fact]
    public async Task ConfiguredUserWithNoLinkedGuildReturnsUnregisteredState()
    {
        var (client, _) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);

        var response = await client.GetAsync("/api/v1/guilds/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MyGuildResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(GuildStateValues.Unregistered, body.State);
        Assert.Null(body.Guild);
    }

    [Fact]
    public async Task RegisteredGuildReturnsOrderedMaskedProjectionForTheLeader()
    {
        var (leaderClient, leaderTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var guildId = Guid.NewGuid();
        var memberUserId = Guid.NewGuid();
        var officerUserId = Guid.NewGuid();
        var token = $"guild-token-{Guid.NewGuid()}";

        FakeTacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(
                guildId,
                "TAG",
                "The Guild",
                42,
                (leaderTacticusUserId, GuildRole.LEADER, 10, 1_780_000_000_000),
                (officerUserId, GuildRole.OFFICER, 8, null),
                (memberUserId, GuildRole.MEMBER, 5, null)
            )
        );

        var registerResponse = await leaderClient.PostAsJsonAsync(
            "/api/v1/guilds/register",
            new RegisterGuildRequest(token),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var response = await leaderClient.GetAsync("/api/v1/guilds/me", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<MyGuildResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(GuildStateValues.Registered, body.State);
        var guild = body.Guild;
        Assert.NotNull(guild);
        Assert.Equal("TAG", guild.Tag);
        Assert.Equal("The Guild", guild.Name);
        Assert.Equal(42, guild.Level);
        Assert.Equal("Leader", guild.CallerRole);
        Assert.True(guild.CanSynchronize);
        Assert.NotNull(guild.LastSyncSucceededAt);

        // Leader, then Officer, then Member — per the Guild Phase 1 member-ordering rule.
        Assert.Equal(3, guild.Members.Count);
        Assert.Equal(["Leader", "Officer", "Member"], guild.Members.Select(member => member.Role));

        // Unlinked members (officer/member here) never leak their full Tacticus user id.
        var unlinkedMembers = guild.Members.Where(member => !member.IsLinked);
        Assert.All(unlinkedMembers, member =>
        {
            Assert.DoesNotContain(officerUserId.ToString(), member.MaskedTacticusUserId);
            Assert.DoesNotContain(memberUserId.ToString(), member.MaskedTacticusUserId);
            Assert.Null(member.LinkedPlayerName);
            Assert.Equal(member.MaskedTacticusUserId, member.DisplayLabel);
        });

        var leaderMember = guild.Members.Single(member => member.Role == "Leader");
        Assert.Equal(1_780_000_000_000, leaderMember.LastActiveInGameOn);
    }

    [Fact]
    public async Task NonLeaderLinkedMemberSeesCanSynchronizeFalse()
    {
        var (leaderClient, leaderTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory, "guild-me-leader");
        var (officerClient, officerTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory, "guild-me-officer");

        var guildId = Guid.NewGuid();
        var token = $"guild-token-{Guid.NewGuid()}";
        FakeTacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(
                guildId,
                "TAG2",
                "Second Guild",
                1,
                (leaderTacticusUserId, GuildRole.LEADER, 10, null),
                (officerTacticusUserId, GuildRole.OFFICER, 8, null)
            )
        );

        var registerResponse = await leaderClient.PostAsJsonAsync(
            "/api/v1/guilds/register",
            new RegisterGuildRequest(token),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var response = await officerClient.GetAsync("/api/v1/guilds/me", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<MyGuildResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(GuildStateValues.Registered, body.State);
        Assert.NotNull(body.Guild);
        Assert.Equal("Officer", body.Guild.CallerRole);
        Assert.False(body.Guild.CanSynchronize);

        var officerMember = body.Guild.Members.Single(member => member.Role == "Officer");
        Assert.True(officerMember.IsLinked);
    }
}
