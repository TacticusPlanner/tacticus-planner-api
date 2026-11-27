using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.Api.Features.TacticusIntegration;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;
using TacticusPlanner.TacticusApi.Models.GuildRaid;
using UpstreamRole = TacticusPlanner.TacticusApi.Models.Guild.GuildRole;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>Claims, names and the gates that need a verified leader (<c>guild-member-verification</c>).</summary>
public sealed class GuildMemberVerificationTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string GuildPath = "/api/v1/guild";
    private const string PlayersPath = "/api/v1/guild/players";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RegistrarIsVerifiedByTheKeyAndSeesTheWaitingClaims()
    {
        var guild = await GuildAsync(UpstreamRole.CO_LEADER, UpstreamRole.MEMBER);

        var leader = await GuildOfAsync(guild.Leader.Client);
        Assert.Equal((GuildClaimStatus.Verified, true, 2), (leader.CallerClaimState, leader.CanManageMembers, leader.PendingClaimCount));
        var member = await GuildOfAsync(guild.Members[1].Client);
        Assert.Equal((GuildClaimStatus.Pending, false, 0), (member.CallerClaimState, member.CanManageMembers, member.PendingClaimCount));

        var refused = await guild.Members[0].Client.GetAsync(PlayersPath, Ct);
        await AssertProblemAsync(refused, HttpStatusCode.Forbidden, "guildVerificationRequired");
    }

    [Fact]
    public async Task PlayersListMembersInRosterOrderThenFormerPlayersWithoutIdentifyingData()
    {
        var guild = await GuildAsync(UpstreamRole.MEMBER);
        var unclaimed = Guid.NewGuid();
        await RosterAsync(guild, (unclaimed, UpstreamRole.OFFICER));
        var former = Guid.NewGuid();
        await RecordHitAsync(guild, former);

        var response = await guild.Leader.Client.GetAsync(PlayersPath, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        var players = (await response.Content.ReadFromJsonAsync<GuildPlayersResponse>(Ct))!.Players;

        Assert.Equal(4, players.Count);
        Assert.Equal([Domain.Guilds.GuildRole.Leader, Domain.Guilds.GuildRole.Officer, Domain.Guilds.GuildRole.Member],
            players.Take(3).Select(player => player.Member!.Role));
        Assert.Null(players[1].Member!.Claim);
        Assert.Null(players[3].Member);
        Assert.NotNull(players[3].LastHitAt);
        Assert.EndsWith(former.ToString()[^4..], players[3].MaskedTacticusUserId);
        Assert.True(players[0].Member!.Claim!.IsCaller);
        Assert.False(players[0].Member!.Claim!.CanReject);
        foreach (var id in new[] { guild.Leader.TacticusUserId, guild.Members[0].TacticusUserId, unclaimed, former })
        {
            Assert.DoesNotContain(id.ToString(), body, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("profileId", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MatchingNameVerifiesAMemberButNeverALeader()
    {
        var guild = await GuildAsync(UpstreamRole.CO_LEADER, UpstreamRole.MEMBER);
        await SyncAsAsync(guild.Members[0], "Dark");
        await SyncAsAsync(guild.Members[1], "Kraken");

        await NameAsync(guild, guild.Members[0], "Dark");
        await NameAsync(guild, guild.Members[1], " kraken ");

        var coLeader = (await GuildTestHelpers.PlayerOfAsync(guild.Leader.Client, guild.Members[0].TacticusUserId)).Member!.Claim!;
        Assert.Equal((GuildClaimStatus.Pending, GuildNameMatch.Matches), (coLeader.State, coLeader.NameMatch));
        var member = (await GuildTestHelpers.PlayerOfAsync(guild.Leader.Client, guild.Members[1].TacticusUserId)).Member!.Claim!;
        Assert.Equal((GuildClaimStatus.Verified, GuildClaimMethod.NameMatch), (member.State, member.Method));
    }

    [Fact]
    public async Task ASyncAfterTheNameWasAssignedVerifiesToo()
    {
        var guild = await GuildAsync(UpstreamRole.OFFICER);
        await NameAsync(guild, guild.Members[0], "Dark");
        Assert.Equal(GuildNameMatch.NoTacticusName, (await ClaimAsync(guild, guild.Members[0])).NameMatch);

        await SyncAsAsync(guild.Members[0], "DARK");

        Assert.Equal(GuildClaimStatus.Verified, (await GuildOfAsync(guild.Members[0].Client)).CallerClaimState);
    }

    [Fact]
    public async Task DuplicateNamesAndAStaleKeyVerifyNobody()
    {
        var guild = await GuildAsync(UpstreamRole.MEMBER, UpstreamRole.MEMBER, UpstreamRole.MEMBER);
        // Both names first: a claim verified before the duplicate appeared would rightly stay verified.
        await NameAsync(guild, guild.Members[0], "Twin");
        await NameAsync(guild, guild.Members[1], "Twin");
        await SyncAsAsync(guild.Members[0], "Twin");
        await SyncAsAsync(guild.Members[1], "Other");

        // Synced, then saved another key: the name of the old sync no longer counts.
        await SyncAsAsync(guild.Members[2], "Late");
        var newKey = $"key-{Guid.NewGuid()}";
        factory.TacticusApi.ConfigurePlayerResponse(newKey, PlayerNamed("Someone"));
        (await guild.Members[2].Client.PutAsJsonAsync("/api/v1/account/tacticus-integration",
            new UpdateTacticusIntegrationRequest(newKey, guild.Members[2].TacticusUserId.ToString()), Ct)).EnsureSuccessStatusCode();
        await NameAsync(guild, guild.Members[2], "Late");

        Assert.Equal(GuildNameMatch.Duplicate, (await ClaimAsync(guild, guild.Members[0])).NameMatch);
        Assert.Equal(GuildClaimStatus.Pending, (await ClaimAsync(guild, guild.Members[0])).State);
        Assert.Equal(GuildClaimStatus.Pending, (await ClaimAsync(guild, guild.Members[1])).State);
        var stale = await ClaimAsync(guild, guild.Members[2]);
        Assert.Equal((GuildClaimStatus.Pending, GuildNameMatch.NoTacticusName), (stale.State, stale.NameMatch));
    }

    [Fact]
    public async Task NameEditorValidatesAndClears()
    {
        var guild = await GuildAsync(UpstreamRole.MEMBER);
        var player = await GuildTestHelpers.PlayerOfAsync(guild.Leader.Client, guild.Members[0].TacticusUserId);
        var path = $"/api/v1/guild/players/{player.PlayerId}/name";

        Assert.Equal(HttpStatusCode.BadRequest,
            (await guild.Leader.Client.PutAsJsonAsync(path, new SetGuildPlayerNameRequest(new string('x', 51)), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await guild.Leader.Client.PutAsJsonAsync(path, new SetGuildPlayerNameRequest("a\u0007b"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await guild.Leader.Client.PutAsJsonAsync($"/api/v1/guild/players/{Guid.NewGuid()}/name",
                new SetGuildPlayerNameRequest("x"), Ct)).StatusCode);

        await NameAsync(guild, guild.Members[0], "  Spaced  ");
        Assert.Equal("Spaced", (await GuildTestHelpers.PlayerOfAsync(guild.Leader.Client, guild.Members[0].TacticusUserId)).AssignedName);
        (await guild.Leader.Client.PutAsJsonAsync(path, new SetGuildPlayerNameRequest("  "), Ct)).EnsureSuccessStatusCode();
        Assert.Null((await GuildTestHelpers.PlayerOfAsync(guild.Leader.Client, guild.Members[0].TacticusUserId)).AssignedName);
    }

    [Fact]
    public async Task ConfirmNeedsTheShownClaimAndNeverTheCallersOwn()
    {
        var guild = await GuildAsync(UpstreamRole.MEMBER);
        var member = (await GuildTestHelpers.PlayerOfAsync(guild.Leader.Client, guild.Members[0].TacticusUserId)).Member!;
        var own = (await GuildTestHelpers.PlayerOfAsync(guild.Leader.Client, guild.Leader.TacticusUserId)).Member!;

        await AssertProblemAsync(
            await DecideAsync(guild.Leader.Client, member.GuildMemberId, "confirm", "0123456789abcdef0123456789abcdef"),
            HttpStatusCode.Conflict, "guildClaimChanged");
        await AssertProblemAsync(
            await DecideAsync(guild.Leader.Client, own.GuildMemberId, "confirm", own.Claim!.ProfileClaimId),
            HttpStatusCode.Forbidden, "forbidden");
        Assert.Equal(HttpStatusCode.NotFound,
            (await DecideAsync(guild.Leader.Client, Guid.NewGuid(), "confirm", member.Claim!.ProfileClaimId)).StatusCode);
        Assert.Equal(GuildClaimStatus.Pending, (await ClaimAsync(guild, guild.Members[0])).State);
    }

    [Fact]
    public async Task ConfirmGivesAnUnnamedPlayerTheSyncedName()
    {
        var guild = await GuildAsync(UpstreamRole.CO_LEADER);
        await SyncAsAsync(guild.Members[0], "Bob");

        await GuildTestHelpers.ConfirmClaimAsync(guild.Leader.Client, guild.Members[0].TacticusUserId);

        var player = await GuildTestHelpers.PlayerOfAsync(guild.Leader.Client, guild.Members[0].TacticusUserId);
        Assert.Equal("Bob", player.AssignedName);
        Assert.Equal((GuildClaimStatus.Verified, GuildClaimMethod.Leader), (player.Member!.Claim!.State, player.Member.Claim.Method));
        Assert.True((await GuildOfAsync(guild.Members[0].Client)).CanManageMembers);
    }

    [Fact]
    public async Task ConfirmAllSkipsNamedPlayersAndChangedClaims()
    {
        var guild = await GuildAsync(UpstreamRole.MEMBER, UpstreamRole.MEMBER, UpstreamRole.MEMBER);
        await SyncAsAsync(guild.Members[1], "Bob");
        await NameAsync(guild, guild.Members[1], "Robert");
        var first = await MemberAsync(guild, guild.Members[0]);
        var named = await MemberAsync(guild, guild.Members[1]);
        var third = await MemberAsync(guild, guild.Members[2]);

        var response = await guild.Leader.Client.PostAsJsonAsync("/api/v1/guild/members/confirm", new ConfirmGuildClaimsRequest(
        [
            new(first.GuildMemberId, first.Claim!.ProfileClaimId),
            new(named.GuildMemberId, named.Claim!.ProfileClaimId),
            new(third.GuildMemberId, "0123456789abcdef0123456789abcdef"),
        ]), Ct);

        var result = (await response.Content.ReadFromJsonAsync<ConfirmGuildClaimsResponse>(Ct))!;
        Assert.Equal([first.GuildMemberId], result.Confirmed);
        Assert.Equal([named.GuildMemberId, third.GuildMemberId], result.Skipped);
        Assert.Equal(GuildClaimStatus.Pending, (await ClaimAsync(guild, guild.Members[1])).State);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await guild.Leader.Client.PostAsJsonAsync("/api/v1/guild/members/confirm", new ConfirmGuildClaimsRequest([]), Ct)).StatusCode);
    }

    [Fact]
    public async Task RejectFreesTheIdForTheRealOwnerAndTheImpostorStaysRejected()
    {
        var guild = await GuildAsync(UpstreamRole.MEMBER);
        var impostor = guild.Members[0];
        var claim = await MemberAsync(guild, impostor);

        (await DecideAsync(guild.Leader.Client, claim.GuildMemberId, "reject", claim.Claim!.ProfileClaimId)).EnsureSuccessStatusCode();

        Assert.Null(await TacticusUserIdHashOfAsync(impostor));
        Assert.Null((await GuildTestHelpers.PlayerOfAsync(guild.Leader.Client, impostor.TacticusUserId)).Member!.Claim);

        // The real owner can now enter the id.
        var (owner, _) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        (await owner.PutAsJsonAsync("/api/v1/account/tacticus-integration",
            new UpdateTacticusIntegrationRequest(null, impostor.TacticusUserId.ToString()), Ct)).EnsureSuccessStatusCode();
        await SyncGuildAsync(guild);
        Assert.Equal(GuildClaimStatus.Pending, (await GuildOfAsync(owner)).CallerClaimState);

        // The impostor coming back after the owner left gets nothing.
        (await owner.PutAsJsonAsync("/api/v1/account/tacticus-integration",
            new UpdateTacticusIntegrationRequest(null, null, ClearTacticusUserId: true), Ct)).EnsureSuccessStatusCode();
        (await impostor.Client.PutAsJsonAsync("/api/v1/account/tacticus-integration",
            new UpdateTacticusIntegrationRequest(null, impostor.TacticusUserId.ToString()), Ct)).EnsureSuccessStatusCode();
        await SyncGuildAsync(guild);
        Assert.Equal(GuildClaimStatus.Rejected, (await GuildOfAsync(impostor.Client)).CallerClaimState);

        // Until a leader confirms the claim after all.
        await GuildTestHelpers.ConfirmClaimAsync(guild.Leader.Client, impostor.TacticusUserId);
        Assert.Equal(GuildClaimStatus.Verified, (await GuildOfAsync(impostor.Client)).CallerClaimState);
    }

    [Fact]
    public async Task OnlyAVerifiedLeaderRejectsAVerifiedLeader()
    {
        var guild = await GuildAsync(UpstreamRole.CO_LEADER);
        await GuildTestHelpers.ConfirmClaimAsync(guild.Leader.Client, guild.Members[0].TacticusUserId);
        var leader = await MemberAsync(guild, guild.Leader, guild.Members[0].Client);
        Assert.False(leader.Claim!.CanReject);

        var response = await DecideAsync(guild.Members[0].Client, leader.GuildMemberId, "reject", leader.Claim.ProfileClaimId);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "forbidden");
        Assert.Equal(GuildClaimStatus.Verified, (await GuildOfAsync(guild.Leader.Client)).CallerClaimState);
        Assert.True((await MemberAsync(guild, guild.Members[0])).Claim!.CanReject);
    }

    [Fact]
    public async Task OnlyAVerifiedCallerReplacesTheKeyOfAGuildWithAVerifiedLeader()
    {
        var guild = await GuildAsync(UpstreamRole.CO_LEADER);
        var newToken = $"guild-token-{Guid.NewGuid()}";
        factory.TacticusApi.ConfigureGuildResponse(newToken, guild.Response);

        var refused = await guild.Members[0].Client.PostAsJsonAsync("/api/v1/guild/register", new RegisterGuildRequest(newToken), Ct);
        await AssertProblemAsync(refused, HttpStatusCode.Forbidden, "guildVerificationRequired");
        Assert.Equal(guild.Token, await StoredTokenAsync(guild));

        await GuildTestHelpers.ConfirmClaimAsync(guild.Leader.Client, guild.Members[0].TacticusUserId);
        (await guild.Members[0].Client.PostAsJsonAsync("/api/v1/guild/register", new RegisterGuildRequest(newToken), Ct))
            .EnsureSuccessStatusCode();
        Assert.Equal(newToken, await StoredTokenAsync(guild));
    }

    [Fact]
    public async Task WithoutAVerifiedLeaderOnTheRosterAnyLeaderStoresTheKeyAndIsVerified()
    {
        var guild = await GuildAsync(UpstreamRole.CO_LEADER);
        // The verified leader left the guild in game.
        var roster = FakeTacticusApi.BuildGuildResponse(
            guild.TacticusGuildId, guild.Tag, "Guild", 1, (guild.Members[0].TacticusUserId, UpstreamRole.LEADER, 5, null));
        var newToken = $"guild-token-{Guid.NewGuid()}";
        factory.TacticusApi.ConfigureGuildResponse(newToken, roster);

        (await guild.Members[0].Client.PostAsJsonAsync("/api/v1/guild/register", new RegisterGuildRequest(newToken), Ct))
            .EnsureSuccessStatusCode();

        var state = await GuildOfAsync(guild.Members[0].Client);
        Assert.Equal((GuildClaimStatus.Verified, true), (state.CallerClaimState, state.CanManageMembers));
    }

    [Fact]
    public async Task ChangingTheIdAndBackEndsVerification()
    {
        var guild = await GuildAsync(UpstreamRole.MEMBER);
        await GuildTestHelpers.ConfirmClaimAsync(guild.Leader.Client, guild.Members[0].TacticusUserId);
        var client = guild.Members[0].Client;

        (await client.PutAsJsonAsync("/api/v1/account/tacticus-integration",
            new UpdateTacticusIntegrationRequest(null, Guid.NewGuid().ToString()), Ct)).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/api/v1/account/tacticus-integration",
            new UpdateTacticusIntegrationRequest(null, guild.Members[0].TacticusUserId.ToString()), Ct)).EnsureSuccessStatusCode();
        await SyncGuildAsync(guild);

        Assert.Equal(GuildClaimStatus.Pending, (await GuildOfAsync(client)).CallerClaimState);
    }

    [Fact]
    public async Task PromotionKeepsVerification()
    {
        var guild = await GuildAsync(UpstreamRole.OFFICER);
        await GuildTestHelpers.ConfirmClaimAsync(guild.Leader.Client, guild.Members[0].TacticusUserId);

        await RosterAsync(guild, overrideRole: UpstreamRole.CO_LEADER);

        var state = await GuildOfAsync(guild.Members[0].Client);
        Assert.Equal((GuildClaimStatus.Verified, true), (state.CallerClaimState, state.CanManageMembers));
    }

    [Fact]
    public async Task AccountPurgeDropsTheNameOfAVerifiedClaimOnly()
    {
        var guild = await GuildAsync(UpstreamRole.MEMBER, UpstreamRole.MEMBER);
        await NameAsync(guild, guild.Members[0], "Verified One");
        await NameAsync(guild, guild.Members[1], "Pending One");
        await GuildTestHelpers.ConfirmClaimAsync(guild.Leader.Client, guild.Members[0].TacticusUserId);

        Assert.Equal(HttpStatusCode.NoContent, (await guild.Members[0].Client.DeleteAsync("/api/v1/account", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await guild.Members[1].Client.DeleteAsync("/api/v1/account", Ct)).StatusCode);

        var verified = await GuildTestHelpers.PlayerOfAsync(guild.Leader.Client, guild.Members[0].TacticusUserId);
        Assert.Equal((null, null), (verified.AssignedName, verified.Member!.Claim));
        var pending = await GuildTestHelpers.PlayerOfAsync(guild.Leader.Client, guild.Members[1].TacticusUserId);
        Assert.Equal("Pending One", pending.AssignedName);
    }

    [Fact]
    public async Task DayOldRosterIsRefreshedByARaidRefreshAndAFailedAttemptWaitsAnHour()
    {
        var guild = await GuildAsync(UpstreamRole.MEMBER);
        await GuildTestHelpers.ConfirmClaimAsync(guild.Leader.Client, guild.Members[0].TacticusUserId);
        factory.TacticusApi.ConfigureGuildRaidResponse(guild.Token, new GuildRaidResponse());

        // A recent roster is not asked for.
        await RaidRefreshAsync(guild);
        var calls = factory.TacticusApi.GuildCallCount(guild.Token);
        Assert.Equal(calls, factory.TacticusApi.GuildCallCount(guild.Token));

        // The member was kicked in game a day later.
        factory.TacticusApi.ConfigureGuildResponse(guild.Token, FakeTacticusApi.BuildGuildResponse(
            guild.TacticusGuildId, guild.Tag, "Guild", 1, (guild.Leader.TacticusUserId, UpstreamRole.LEADER, 10, null)));
        await AgeRosterAsync(guild, TimeSpan.FromHours(25));
        await RaidRefreshAsync(guild);

        Assert.Equal(calls + 1, factory.TacticusApi.GuildCallCount(guild.Token));
        Assert.Equal(MyGuildState.Unregistered,
            (await (await guild.Members[0].Client.GetAsync(GuildPath, Ct)).Content.ReadFromJsonAsync<MyGuildResponse>(Ct))!.State);

        // A failing attempt keeps the refresh and is not retried within the hour.
        factory.TacticusApi.ConfigureGuildUnavailable(guild.Token);
        await AgeRosterAsync(guild, TimeSpan.FromHours(25));
        await RaidRefreshAsync(guild);
        await RaidRefreshAsync(guild);
        Assert.Equal(calls + 2, factory.TacticusApi.GuildCallCount(guild.Token));
    }

    private sealed record Account(HttpClient Client, Guid TacticusUserId, string Key, Guid ProfileId);

    private sealed record TestGuild(
        Account Leader, IReadOnlyList<Account> Members, string Token, Guid TacticusGuildId, string Tag,
        TacticusApi.Models.Guild.GuildResponse Response)
    {
        public List<(Guid Id, UpstreamRole Role)> Roster { get; } = [];
    }

    /// <summary>A guild registered by its Leader, with one account per other role.</summary>
    private async Task<TestGuild> GuildAsync(params UpstreamRole[] roles)
    {
        var leader = await AccountAsync();
        var members = new List<Account>();
        foreach (var _ in roles)
        {
            members.Add(await AccountAsync());
        }

        var token = $"guild-token-{Guid.NewGuid()}";
        var guildId = Guid.NewGuid();
        var tag = $"V{Guid.NewGuid():N}"[..10];
        var entries = new[] { (leader.TacticusUserId, UpstreamRole.LEADER) }
            .Concat(members.Select((member, index) => (member.TacticusUserId, roles[index])))
            .ToList();
        var response = Roster(guildId, tag, entries);
        factory.TacticusApi.ConfigureGuildResponse(token, response);
        (await leader.Client.PostAsJsonAsync("/api/v1/guild/register", new RegisterGuildRequest(token), Ct)).EnsureSuccessStatusCode();
        var guild = new TestGuild(leader, members, token, guildId, tag, response);
        guild.Roster.AddRange(entries);
        return guild;
    }

    private async Task<Account> AccountAsync()
    {
        var (client, tacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var key = $"key-{Guid.NewGuid()}";
        factory.TacticusApi.ConfigurePlayerResponse(key, PlayerNamed(FakeTacticusApi.PlayerName));
        var saved = await client.PutAsJsonAsync("/api/v1/account/tacticus-integration",
            new UpdateTacticusIntegrationRequest(key, tacticusUserId.ToString()), Ct);
        saved.EnsureSuccessStatusCode();
        var profileId = (await saved.Content.ReadFromJsonAsync<UpdateTacticusIntegrationResponse>(Ct))!.ProfileId;
        return new Account(client, tacticusUserId, key, profileId);
    }

    private static TacticusApi.Models.Guild.GuildResponse Roster(Guid guildId, string tag, IEnumerable<(Guid Id, UpstreamRole Role)> entries) =>
        FakeTacticusApi.BuildGuildResponse(guildId, tag, "Guild", 1,
            [.. entries.Select(entry => (entry.Id, entry.Role, 10, (long?)null))]);

    /// <summary>Syncs the guild with extra members, or the first member in another role.</summary>
    private async Task RosterAsync(TestGuild guild, (Guid Id, UpstreamRole Role)? extra = null, UpstreamRole? overrideRole = null)
    {
        if (extra is { } added)
        {
            guild.Roster.Add(added);
        }

        if (overrideRole is { } role)
        {
            guild.Roster[1] = (guild.Roster[1].Id, role);
        }

        factory.TacticusApi.ConfigureGuildResponse(guild.Token, Roster(guild.TacticusGuildId, guild.Tag, guild.Roster));
        await SyncGuildAsync(guild);
    }

    private async Task SyncGuildAsync(TestGuild guild)
    {
        await GuildTestHelpers.ExpireSyncCooldownAsync(factory);
        (await guild.Leader.Client.PostAsync("/api/v1/guild/sync", null, Ct)).EnsureSuccessStatusCode();
    }

    private static TacticusApi.Models.Player.PlayerResponse PlayerNamed(string name)
    {
        var response = FakeTacticusApi.BuildPlayerResponse();
        response.Player.Details.Name = name;
        return response;
    }

    private async Task SyncAsAsync(Account account, string name)
    {
        factory.TacticusApi.ConfigurePlayerResponse(account.Key, PlayerNamed(name));
        (await account.Client.PostAsync("/api/v1/player-data/sync", null, Ct)).EnsureSuccessStatusCode();
    }

    private static async Task NameAsync(TestGuild guild, Account account, string name)
    {
        var player = await GuildTestHelpers.PlayerOfAsync(guild.Leader.Client, account.TacticusUserId);
        (await guild.Leader.Client.PutAsJsonAsync(
            $"/api/v1/guild/players/{player.PlayerId}/name", new SetGuildPlayerNameRequest(name), Ct)).EnsureSuccessStatusCode();
    }

    private static async Task<GuildPlayerMembership> MemberAsync(TestGuild guild, Account account, HttpClient? viewer = null) =>
        (await GuildTestHelpers.PlayerOfAsync(viewer ?? guild.Leader.Client, account.TacticusUserId)).Member!;

    private static async Task<GuildPlayerClaim> ClaimAsync(TestGuild guild, Account account) =>
        (await MemberAsync(guild, account)).Claim!;

    private static Task<HttpResponseMessage> DecideAsync(HttpClient client, Guid memberId, string decision, string claimId) =>
        client.PostAsJsonAsync($"/api/v1/guild/members/{memberId}/{decision}", new GuildClaimDecisionRequest(claimId), Ct);

    private static async Task<RegisteredGuildResponse> GuildOfAsync(HttpClient client) =>
        (await (await client.GetAsync(GuildPath, Ct)).Content.ReadFromJsonAsync<MyGuildResponse>(Ct))!.Guild!;

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string issueCode)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Contains($"\"{issueCode}\"", await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task RaidRefreshAsync(TestGuild guild)
    {
        await using (var db = Db())
        {
            var expired = DateTimeOffset.UtcNow - GuildRaidStatusService.CooldownWindow - TimeSpan.FromSeconds(1);
            await db.GuildRaidSyncStates.Where(state => state.Guild!.Tag == guild.Tag)
                .ExecuteUpdateAsync(set => set.SetProperty(state => state.LastAttemptedAt, expired), Ct);
        }

        (await guild.Leader.Client.PostAsync("/api/v1/guild/raid-status/refresh", null, Ct)).EnsureSuccessStatusCode();
    }

    private async Task AgeRosterAsync(TestGuild guild, TimeSpan age)
    {
        await using var db = Db();
        var at = DateTimeOffset.UtcNow - age;
        await db.Guilds.Where(entity => entity.Tag == guild.Tag).ExecuteUpdateAsync(set => set
            .SetProperty(entity => entity.LastSyncSucceededAt, at)
            .SetProperty(entity => entity.LastSyncAttemptedAt, at), Ct);
    }

    private async Task RecordHitAsync(TestGuild guild, Guid player)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var hash = scope.ServiceProvider.GetRequiredService<Persistence.Encryption.IColumnHashService>();
        var guildId = await db.Guilds.Where(entity => entity.Tag == guild.Tag).Select(entity => entity.Id).SingleAsync(Ct);
        var entry = new GuildRaidEntry
        {
            UserId = player,
            Tier = 1,
            Set = 0,
            EncounterType = TacticusApi.Models.GuildRaid.EncounterType.Boss,
            UnitId = "guildBoss:1",
            Rarity = TacticusApi.Models.Shared.Rarity.Common,
            RemainingHp = 1,
            MaxHp = 2,
            DamageDealt = 1,
            DamageType = TacticusApi.Models.GuildRaid.DamageType.Battle,
            CompletedOn = 1_780_000_100,
            HeroDetails = [],
            GlobalConfigHash = "config",
        };
        Assert.True(GuildRaidEntryNormalizer.TryBuildPastAttack(entry, hash, out var attack));
        var season = new Domain.GuildRaids.GuildRaidSeason
        {
            Id = Domain.GuildRaids.GuildRaidSeasonId.From(Guid.CreateVersion7()),
            GuildId = guildId,
            SeasonNumber = 90,
            SeasonConfigId = "guild_boss_season_config_1",
            ObservedAt = DateTimeOffset.UtcNow,
        };
        season.Attacks.Add(attack);
        db.GuildRaidSeasons.Add(season);
        await db.SaveChangesAsync(Ct);
        await scope.ServiceProvider.GetRequiredService<GuildPlayerRegistry>()
            .AddAsync(guildId, [(attack.TacticusUserIdHash, attack.TacticusUserIdSuffix)], Ct);
    }

    private async Task<TacticusUserIdHash?> TacticusUserIdHashOfAsync(Account account)
    {
        await using var db = Db();
        var profileId = ProfileId.From(account.ProfileId);
        return await db.Profiles.IgnoreQueryFilters()
            .Where(profile => profile.Id == profileId)
            .Select(profile => profile.TacticusUserIdHash)
            .SingleAsync(Ct);
    }

    private async Task<string?> StoredTokenAsync(TestGuild guild)
    {
        await using var db = Db();
        return await db.Guilds.Where(entity => entity.Tag == guild.Tag).Select(entity => entity.GuildApiToken).SingleAsync(Ct);
    }

    private PlannerDbContext Db() => factory.Services.CreateScope().ServiceProvider.GetRequiredService<PlannerDbContext>();
}
