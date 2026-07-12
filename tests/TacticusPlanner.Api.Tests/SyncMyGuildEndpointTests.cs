using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.TacticusApi.Models.Guild;

namespace TacticusPlanner.Api.Tests;

public sealed class SyncMyGuildEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task UnauthenticatedRequestIsRejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.NoAuthHeader, "1");

        var response = await client.PostAsync("/api/v1/guilds/me/sync", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnprovisionedAccountIsNotFound()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, $"guild-sync-{Guid.NewGuid()}");

        var response = await client.PostAsync("/api/v1/guilds/me/sync", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NoLinkedGuildIsForbidden()
    {
        var (client, _) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);

        var response = await client.PostAsync("/api/v1/guilds/me/sync", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DemotedCallerIsForbiddenAndNoDataIsChanged()
    {
        var (client, tacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var token = $"guild-token-{Guid.NewGuid()}";
        var guildId = Guid.NewGuid();

        FakeTacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(guildId, "TAG", "Original", 1, (tacticusUserId, GuildRole.LEADER, 10, null))
        );
        await client.PostAsJsonAsync("/api/v1/guilds/register", new RegisterGuildRequest(token), TestContext.Current.CancellationToken);

        // Caller is demoted below Co-Leader in the fresh roster — sync must reject and persist nothing.
        FakeTacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(guildId, "TAG", "ShouldNotApply", 99, (tacticusUserId, GuildRole.MEMBER, 10, null))
        );

        var syncResponse = await client.PostAsync("/api/v1/guilds/me/sync", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, syncResponse.StatusCode);

        var afterResponse = await client.GetAsync("/api/v1/guilds/me", TestContext.Current.CancellationToken);
        var after = await afterResponse.Content.ReadFromJsonAsync<MyGuildResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(after);
        Assert.NotNull(after.Guild);
        Assert.Equal("Original", after.Guild.Name);
        Assert.Equal(1, after.Guild.Level);
        Assert.Equal("Leader", after.Guild.CallerRole);
    }

    [Fact]
    public async Task SuccessfulSyncRefreshesGuildDataUsingTheStoredToken()
    {
        var (client, tacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var token = $"guild-token-{Guid.NewGuid()}";
        var guildId = Guid.NewGuid();

        FakeTacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(guildId, "TAG", "Original", 1, (tacticusUserId, GuildRole.LEADER, 10, null))
        );
        await client.PostAsJsonAsync("/api/v1/guilds/register", new RegisterGuildRequest(token), TestContext.Current.CancellationToken);

        // The sync endpoint takes no request body/token — it can only succeed if it reads back the token
        // persisted at registration, since FakeTacticusApi only answers for tokens explicitly configured.
        FakeTacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(guildId, "TAG", "Updated Name", 5, (tacticusUserId, GuildRole.LEADER, 20, null))
        );

        var response = await client.PostAsync("/api/v1/guilds/me/sync", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RegisteredGuildResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("Updated Name", body.Name);
        Assert.Equal(5, body.Level);
        Assert.NotNull(body.LastSyncSucceededAt);
    }

    [Fact]
    public async Task DepartedMembersAreRemovedOnSync()
    {
        var (client, tacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var departedTacticusUserId = Guid.NewGuid();
        var token = $"guild-token-{Guid.NewGuid()}";
        var guildId = Guid.NewGuid();

        FakeTacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(
                guildId,
                "TAG",
                "Guild",
                1,
                (tacticusUserId, GuildRole.LEADER, 10, null),
                (departedTacticusUserId, GuildRole.MEMBER, 5, null)
            )
        );
        var registerResponse = await client.PostAsJsonAsync(
            "/api/v1/guilds/register",
            new RegisterGuildRequest(token),
            TestContext.Current.CancellationToken
        );
        var registerBody = await registerResponse.Content.ReadFromJsonAsync<RegisteredGuildResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(registerBody);
        Assert.Equal(2, registerBody.Members.Count);

        // The member departs the upstream guild before the next sync.
        FakeTacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(guildId, "TAG", "Guild", 1, (tacticusUserId, GuildRole.LEADER, 10, null))
        );

        var syncResponse = await client.PostAsync("/api/v1/guilds/me/sync", null, TestContext.Current.CancellationToken);
        var syncBody = await syncResponse.Content.ReadFromJsonAsync<RegisteredGuildResponse>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, syncResponse.StatusCode);
        Assert.NotNull(syncBody);
        Assert.Single(syncBody.Members);
        Assert.Equal("Leader", syncBody.Members[0].Role);
    }

    [Fact]
    public async Task LinkedMembersLastActiveInPlannerReflectsTheirAccountLastSeenAt()
    {
        var (leaderClient, leaderTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory, "guild-sync-leader");
        // Calling /me during setup (inside CreateGuildReadyClientAsync) already stamps this profile's
        // Account.LastSeenAt, so once linked it should carry a non-null last-active-in-Planner value.
        var (_, memberTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory, "guild-sync-member");

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

        var response = await leaderClient.PostAsJsonAsync(
            "/api/v1/guilds/register",
            new RegisterGuildRequest(token),
            TestContext.Current.CancellationToken
        );
        var body = await response.Content.ReadFromJsonAsync<RegisteredGuildResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        var memberEntry = body.Members.Single(member => member.Role == "Member");
        Assert.True(memberEntry.IsLinked);
        Assert.NotNull(memberEntry.LastActiveInPlannerOn);
    }
}
