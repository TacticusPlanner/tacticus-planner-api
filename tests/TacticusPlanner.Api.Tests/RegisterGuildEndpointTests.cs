using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.CurrentUser;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.TacticusApi.Models.Guild;

namespace TacticusPlanner.Api.Tests;

public sealed class RegisterGuildEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task UnauthenticatedRequestIsRejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.NoAuthHeader, "1");

        var response = await client.PostAsJsonAsync(
            "/api/v1/guilds/register",
            new RegisterGuildRequest("token"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnprovisionedAccountIsNotFound()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, $"guild-register-{Guid.NewGuid()}");

        var response = await client.PostAsJsonAsync(
            "/api/v1/guilds/register",
            new RegisterGuildRequest("token"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BlankTokenIsRejected()
    {
        var (client, _) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/guilds/register",
            new RegisterGuildRequest("   "),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NoTacticusUserIdConfiguredIsRejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, $"guild-register-{Guid.NewGuid()}");
        await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        var response = await client.PostAsJsonAsync(
            "/api/v1/guilds/register",
            new RegisterGuildRequest("some-token"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CallerAbsentFromFreshRosterIsForbidden()
    {
        var (client, _) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var token = $"guild-token-{Guid.NewGuid()}";

        FakeTacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(
                Guid.NewGuid(),
                "TAG",
                "Some Guild",
                1,
                (Guid.NewGuid(), GuildRole.LEADER, 10, null)
            )
        );

        var response = await client.PostAsJsonAsync(
            "/api/v1/guilds/register",
            new RegisterGuildRequest(token),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CallerBelowCoLeaderIsForbidden()
    {
        var (client, tacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var token = $"guild-token-{Guid.NewGuid()}";

        FakeTacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(
                Guid.NewGuid(),
                "TAG",
                "Some Guild",
                1,
                (tacticusUserId, GuildRole.OFFICER, 10, null)
            )
        );

        var response = await client.PostAsJsonAsync(
            "/api/v1/guilds/register",
            new RegisterGuildRequest(token),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MalformedUpstreamDataIsRejected()
    {
        var (client, tacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var token = $"guild-token-{Guid.NewGuid()}";

        // Duplicate member ids in the upstream response are invalid, regardless of the caller's own role.
        FakeTacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(
                Guid.NewGuid(),
                "TAG",
                "Some Guild",
                1,
                (tacticusUserId, GuildRole.LEADER, 10, null),
                (tacticusUserId, GuildRole.LEADER, 10, null)
            )
        );

        var response = await client.PostAsJsonAsync(
            "/api/v1/guilds/register",
            new RegisterGuildRequest(token),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpstreamRejectedTokenIsBadRequest()
    {
        var (client, _) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var token = $"guild-token-{Guid.NewGuid()}";
        FakeTacticusApi.ConfigureGuildRejection(token, HttpStatusCode.Unauthorized);

        var response = await client.PostAsJsonAsync(
            "/api/v1/guilds/register",
            new RegisterGuildRequest(token),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpstreamUnavailableReturnsServiceUnavailable()
    {
        var (client, _) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var token = $"guild-token-{Guid.NewGuid()}";
        FakeTacticusApi.ConfigureGuildUnavailable(token);

        var response = await client.PostAsJsonAsync(
            "/api/v1/guilds/register",
            new RegisterGuildRequest(token),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task SuccessfulRegistrationPersistsAndReturnsTheGuild()
    {
        var (client, tacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var token = $"guild-token-{Guid.NewGuid()}";
        var guildId = Guid.NewGuid();

        FakeTacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(
                guildId,
                "TAG",
                "My Guild",
                7,
                (tacticusUserId, GuildRole.LEADER, 10, null)
            )
        );

        var response = await client.PostAsJsonAsync(
            "/api/v1/guilds/register",
            new RegisterGuildRequest(token),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RegisteredGuildResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("TAG", body.Tag);
        Assert.Equal("My Guild", body.Name);
        Assert.Equal(7, body.Level);
        Assert.Equal(guildId, body.TacticusGuildId);
        Assert.Equal("Leader", body.CallerRole);
        Assert.True(body.CanSynchronize);
        Assert.Single(body.Members);
    }

    [Fact]
    public async Task ReRegisteringTheSameUpstreamGuildUpdatesRatherThanDuplicating()
    {
        var (client, tacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var token = $"guild-token-{Guid.NewGuid()}";
        var guildId = Guid.NewGuid();

        FakeTacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(guildId, "TAG", "Original Name", 1, (tacticusUserId, GuildRole.LEADER, 10, null))
        );
        var first = await client.PostAsJsonAsync(
            "/api/v1/guilds/register",
            new RegisterGuildRequest(token),
            TestContext.Current.CancellationToken
        );
        var firstBody = await first.Content.ReadFromJsonAsync<RegisteredGuildResponse>(TestContext.Current.CancellationToken);

        FakeTacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(guildId, "TAG", "Renamed", 2, (tacticusUserId, GuildRole.LEADER, 10, null))
        );
        var second = await client.PostAsJsonAsync(
            "/api/v1/guilds/register",
            new RegisterGuildRequest(token),
            TestContext.Current.CancellationToken
        );
        var secondBody = await second.Content.ReadFromJsonAsync<RegisteredGuildResponse>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.NotNull(firstBody);
        Assert.NotNull(secondBody);
        Assert.Equal(firstBody.GuildId, secondBody.GuildId);
        Assert.Equal("Renamed", secondBody.Name);
        Assert.Equal(2, secondBody.Level);
    }

    [Fact]
    public async Task FirstRegistrationReportsGuildRegistered()
    {
        var (client, tacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var analyticsId = await GetAnalyticsIdAsync(client);
        var token = $"guild-token-{Guid.NewGuid()}";

        FakeTacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(
                Guid.NewGuid(),
                "TAG",
                "Some Guild",
                1,
                (tacticusUserId, GuildRole.LEADER, 10, null)
            )
        );

        var response = await client.PostAsJsonAsync(
            "/api/v1/guilds/register",
            new RegisterGuildRequest(token),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(RecordingProductAnalytics.GetEvents(analyticsId), eventName => eventName == "guild_registered");
    }

    [Fact]
    public async Task ReRegisteringAnExistingGuildDoesNotReReportGuildRegistered()
    {
        var (client, tacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var analyticsId = await GetAnalyticsIdAsync(client);
        var token = $"guild-token-{Guid.NewGuid()}";
        var guildId = Guid.NewGuid();

        FakeTacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(guildId, "TAG", "Some Guild", 1, (tacticusUserId, GuildRole.LEADER, 10, null))
        );
        await client.PostAsJsonAsync("/api/v1/guilds/register", new RegisterGuildRequest(token), TestContext.Current.CancellationToken);

        FakeTacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(guildId, "TAG", "Renamed", 2, (tacticusUserId, GuildRole.LEADER, 10, null))
        );
        var second = await client.PostAsJsonAsync(
            "/api/v1/guilds/register",
            new RegisterGuildRequest(token),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Single(RecordingProductAnalytics.GetEvents(analyticsId), eventName => eventName == "guild_registered");
    }

    [Fact]
    public async Task RejectedRegistrationDoesNotReportGuildRegistered()
    {
        var (client, _) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var analyticsId = await GetAnalyticsIdAsync(client);
        var token = $"guild-token-{Guid.NewGuid()}";
        FakeTacticusApi.ConfigureGuildRejection(token, HttpStatusCode.Unauthorized);

        await client.PostAsJsonAsync("/api/v1/guilds/register", new RegisterGuildRequest(token), TestContext.Current.CancellationToken);

        Assert.DoesNotContain("guild_registered", RecordingProductAnalytics.GetEvents(analyticsId));
    }

    [Fact]
    public async Task NoEmittedEventCarriesPersonalOrGuildData()
    {
        var (client, tacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var analyticsId = await GetAnalyticsIdAsync(client);
        var token = $"guild-token-{Guid.NewGuid()}";

        FakeTacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(
                Guid.NewGuid(),
                "TAG",
                "A Very Identifiable Guild Name",
                1,
                (tacticusUserId, GuildRole.LEADER, 10, null)
            )
        );
        await client.PostAsJsonAsync("/api/v1/guilds/register", new RegisterGuildRequest(token), TestContext.Current.CancellationToken);

        // IProductAnalytics.GuildRegistered(string analyticsId) has no properties parameter at all - there
        // is no channel for the guild name, tacticus user id, or account id to travel through. This asserts
        // the recorded event carries only the event name and analytics id, and that the analytics id itself
        // (the one value that does travel) doesn't encode either.
        var events = RecordingProductAnalytics.GetEvents(analyticsId);
        Assert.Single(events, eventName => eventName == "guild_registered");
        Assert.DoesNotContain("A Very Identifiable Guild Name", analyticsId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(tacticusUserId.ToString(), analyticsId, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> GetAnalyticsIdAsync(HttpClient client)
    {
        var body = await (await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<CurrentUserResponse>(TestContext.Current.CancellationToken);
        return body!.AnalyticsId;
    }

    [Fact]
    public async Task AlreadyConfiguredProfileIsLinkedImmediatelyOnRegistration()
    {
        var (leaderClient, leaderTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory, "guild-register-leader");
        var (_, memberTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory, "guild-register-member");

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
    }
}
