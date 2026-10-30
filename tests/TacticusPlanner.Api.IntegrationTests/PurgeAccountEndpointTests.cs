using System.Net;
using TacticusPlanner.Api.Features.AccountManagement;
using TacticusPlanner.Api.Features.CurrentUser;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.TacticusApi.Models.Guild;

namespace TacticusPlanner.Api.IntegrationTests;

public sealed class PurgeAccountEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task PurgeDeletesTheAccountAndSubsequentMeReprovisionsAFreshOne()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, NewSubject());

        var before = await (await client.GetAsync("/api/v1/account", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<CurrentUserResponse>(TestContext.Current.CancellationToken);

        var purgeResponse = await client.DeleteAsync("/api/v1/account", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, purgeResponse.StatusCode);

        var after = await (await client.GetAsync("/api/v1/account", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<CurrentUserResponse>(TestContext.Current.CancellationToken);

        Assert.NotEqual(before!.ApplicationUserId, after!.ApplicationUserId);
        Assert.False(after.HasCompletedOnboarding);
    }

    [Fact]
    public async Task PurgeIsIdempotentForAnAccountThatWasNeverProvisioned()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, NewSubject());

        var response = await client.DeleteAsync("/api/v1/account", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task PurgeKeepsTheSignInAccountByDefault()
    {
        var objectId = Guid.NewGuid().ToString();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, NewSubject());
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.ObjectIdHeader, objectId);

        var response = await client.DeleteAsync("/api/v1/account", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(factory.ExternalIdentityDeleter.WasRequested(objectId));
        Assert.Equal("false", SignInAccountDeleted(response));
    }

    [Fact]
    public async Task PurgeAlsoDeletesTheSignInAccountWhenAsked()
    {
        var objectId = Guid.NewGuid().ToString();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, NewSubject());
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.ObjectIdHeader, objectId);

        var response = await client.DeleteAsync(
            "/api/v1/account?deleteSignInAccount=true",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(factory.ExternalIdentityDeleter.WasRequested(objectId));
        Assert.Equal("true", SignInAccountDeleted(response));
    }

    [Fact]
    public async Task PurgeReportsWhenTheSignInAccountCouldNotBeDeleted()
    {
        var objectId = Guid.NewGuid().ToString();
        factory.ExternalIdentityDeleter.Refuse(objectId);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, NewSubject());
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.ObjectIdHeader, objectId);

        var response = await client.DeleteAsync(
            "/api/v1/account?deleteSignInAccount=true",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(factory.ExternalIdentityDeleter.WasRequested(objectId));
        Assert.Equal("false", SignInAccountDeleted(response));
    }

    [Fact]
    public async Task PurgeClearsTheGuildVisibleCopiesOfTheProfile()
    {
        var ct = TestContext.Current.CancellationToken;
        var (leaderClient, leaderTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var (memberClient, memberTacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        (await memberClient.PutAsJsonAsync("/api/v1/account/display-name", new { displayName = "Departing" }, ct))
            .EnsureSuccessStatusCode();
        var token = $"guild-token-{Guid.NewGuid()}";
        factory.TacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(
                Guid.NewGuid(), $"P{Guid.NewGuid():N}"[..10], "Guild", 1,
                (leaderTacticusUserId, GuildRole.LEADER, 10, null),
                (memberTacticusUserId, GuildRole.MEMBER, 5, null)));
        (await leaderClient.PostAsJsonAsync("/api/v1/guild/register", new RegisterGuildRequest(token), ct)).EnsureSuccessStatusCode();
        var before = await MemberAsync(leaderClient, memberTacticusUserId);
        Assert.Equal("Departing", before.LinkedPlayerName);
        Assert.NotNull(before.LastActiveInPlannerOn);

        (await memberClient.DeleteAsync("/api/v1/account", ct)).EnsureSuccessStatusCode();

        var after = await MemberAsync(leaderClient, memberTacticusUserId);
        Assert.False(after.IsLinked);
        Assert.Null(after.LinkedPlayerName);
        Assert.Null(after.LastActiveInPlannerOn);
    }

    private static async Task<GuildMemberSummary> MemberAsync(HttpClient leaderClient, Guid tacticusUserId)
    {
        var guild = await (await leaderClient.GetAsync("/api/v1/guild", TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<MyGuildResponse>(TestContext.Current.CancellationToken);
        return Assert.Single(guild!.Guild!.Members, member => member.MaskedTacticusUserId.EndsWith(tacticusUserId.ToString()[^4..], StringComparison.Ordinal));
    }

    private static string? SignInAccountDeleted(HttpResponseMessage response) =>
        response.Headers.TryGetValues(PurgeAccountEndpoint.SignInAccountDeletedHeader, out var values) ? values.Single() : null;

    private static string NewSubject() => $"purge-{Guid.NewGuid()}";
}
