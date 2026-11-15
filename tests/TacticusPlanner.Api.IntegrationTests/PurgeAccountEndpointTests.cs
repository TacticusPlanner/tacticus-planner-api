using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.AccountManagement;
using TacticusPlanner.Api.Features.CurrentUser;
using TacticusPlanner.Api.Features.EventShopPlans;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;
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
    public async Task PurgeDeletesTheAccountsEventShopPlans()
    {
        var ct = TestContext.Current.CancellationToken;
        var subject = NewSubject();
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory, subject);
        var octoberPlan = await client.PutAsJsonAsync(
            "/api/v1/event-shop-plans/october-2026",
            new PutEventShopPlanRequest(0, [new EventShopCartEntryRequest(1, "THU", 1, "shards_astraYarrick", 1, 0, null)]),
            ct);
        Assert.Equal(HttpStatusCode.OK, octoberPlan.StatusCode);
        ProfileId profileId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
            profileId = await db.Profiles.IgnoreQueryFilters()
                .Where(profile => profile.Subject == subject).Select(profile => profile.Id).SingleAsync(ct);
            Assert.NotEmpty(await db.EventShopPlans.IgnoreQueryFilters()
                .Where(plan => plan.ProfileId == profileId).ToListAsync(ct));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/v1/account", ct)).StatusCode);

        using var verify = factory.Services.CreateScope();
        Assert.Empty(await verify.ServiceProvider.GetRequiredService<PlannerDbContext>().EventShopPlans.IgnoreQueryFilters()
            .Where(plan => plan.ProfileId == profileId).ToListAsync(ct));
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
