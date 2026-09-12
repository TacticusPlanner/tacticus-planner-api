using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.Domain.GuildRaids.Enums;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.Persistence;
using TacticusPlanner.TacticusApi.Models.Guild;
using TacticusPlanner.TacticusApi.Models.GuildRaid;
using TacticusPlanner.TacticusApi.Models.Shared;

namespace TacticusPlanner.Api.Tests;

public sealed class GetMyGuildRaidStatusEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string StatusPath = "/api/v1/guilds/me/raid-status";
    private const string RefreshPath = "/api/v1/guilds/me/raid-status/refresh";

    [Fact]
    public async Task UnprovisionedProfileIsNotFoundAndUnregisteredProfileConflicts()
    {
        var unprovisioned = factory.CreateClient();
        unprovisioned.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, $"raid-{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, (await unprovisioned.GetAsync(
            StatusPath, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await unprovisioned.PostAsync(
            RefreshPath, null, TestContext.Current.CancellationToken)).StatusCode);

        var (unregistered, _) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        Assert.Equal(HttpStatusCode.Conflict, (await unregistered.GetAsync(
            StatusPath, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await unregistered.PostAsync(
            RefreshPath, null, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task NeverSynchronizedOrTokenlessGuildConflictsWithoutRaidCall()
    {
        var ready = await RegisterAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
            var guild = await db.Guilds.SingleAsync(entity => entity.Tag == ready.Tag, TestContext.Current.CancellationToken);
            guild.LastSyncSucceededAt = null;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await ready.Client.GetAsync(
            StatusPath, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ready.Client.PostAsync(
            RefreshPath, null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(0, FakeTacticusApi.GuildRaidCallCount(ready.Token));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
            var guild = await db.Guilds.SingleAsync(entity => entity.Tag == ready.Tag, TestContext.Current.CancellationToken);
            guild.LastSyncSucceededAt = DateTimeOffset.UtcNow;
            guild.GuildApiToken = null;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await ready.Client.GetAsync(
            StatusPath, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ready.Client.PostAsync(
            RefreshPath, null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(0, FakeTacticusApi.GuildRaidCallCount(ready.Token));
    }

    [Fact]
    public async Task ReadyGuildWithNoObservationYetConflictsOnReadWithoutUpstreamCall()
    {
        var ready = await RegisterAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await ready.Client.GetAsync(
            StatusPath, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(0, FakeTacticusApi.GuildRaidCallCount(ready.Token));
    }

    [Fact]
    public async Task ForcedRefreshPersistsAndSubsequentReadsNeverCallUpstream()
    {
        var ready = await RegisterAsync();
        FakeTacticusApi.ConfigureGuildRaidResponse(ready.Token, BuildActiveResponse());

        var refreshed = await PostRefreshAsync(ready.Client);
        var first = await ready.Client.GetFromJsonAsync<GuildRaidStatusResponse>(
            StatusPath, TestContext.Current.CancellationToken);
        var second = await ready.Client.GetFromJsonAsync<GuildRaidStatusResponse>(
            StatusPath, TestContext.Current.CancellationToken);

        Assert.NotNull(refreshed);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(GuildRaidObservationState.Active, refreshed.State);
        Assert.Equal(GuildRaidFreshness.Fresh, refreshed.Freshness);
        Assert.Equal(refreshed.ObservedAt, first.ObservedAt);
        Assert.Equal(GuildRaidFreshness.Fresh, first.Freshness);
        Assert.Equal(first.ObservedAt, second.ObservedAt);
        Assert.Equal(1, FakeTacticusApi.GuildRaidCallCount(ready.Token));
        Assert.NotNull(first.Season);
        Assert.DoesNotContain("guildApiToken", await JsonContent(first));
        Assert.DoesNotContain("attacks", await JsonContent(first));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var guildId = await db.Guilds.Where(guild => guild.Tag == ready.Tag)
            .Select(guild => guild.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
        var persistedSeason = Assert.Single(await db.GuildRaidSeasons
            .Where(season => season.GuildId == guildId && season.SeasonNumber == 77)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await db.GuildRaidAttacks.Where(attack => attack.GuildRaidSeasonId == persistedSeason.Id)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SuffixlessUnitIdUsesCatalogProgressionAndRepairsPersistedAttack()
    {
        var ready = await RegisterAsync();
        var upstream = BuildActiveResponse(includeProgressionSuffix: false, useAdvancedProgression: true);
        FakeTacticusApi.ConfigureGuildRaidResponse(ready.Token, upstream);

        var first = await PostRefreshAsync(ready.Client);
        Assert.NotNull(first?.Season);
        Assert.True(first.Season.Boss.ProgressionIndex > 1);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
            var guildId = await db.Guilds.Where(guild => guild.Tag == ready.Tag)
                .Select(guild => guild.Id)
                .SingleAsync(TestContext.Current.CancellationToken);
            var state = await db.GuildRaidSyncStates.SingleAsync(
                entity => entity.GuildId == guildId,
                TestContext.Current.CancellationToken);
            var attack = await db.GuildRaidAttacks.SingleAsync(
                entity => entity.GuildRaidSeasonId == state.ActiveSeasonId,
                TestContext.Current.CancellationToken);
            Assert.Equal(first.Season.Boss.ProgressionIndex, attack.ProgressionIndex);

            attack.ProgressionIndex = 1;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var retained = await ready.Client.GetFromJsonAsync<GuildRaidStatusResponse>(
            StatusPath,
            TestContext.Current.CancellationToken);
        Assert.Equal(first.Season.Boss.ProgressionIndex, retained?.Season?.Boss.ProgressionIndex);

        await ExpireCooldownAsync(ready.Tag);
        var second = await PostRefreshAsync(ready.Client);
        Assert.Equal(first.Season.Boss.ProgressionIndex, second?.Season?.Boss.ProgressionIndex);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var verificationSeasonId = await verificationDb.Guilds
            .Where(guild => guild.Tag == ready.Tag)
            .Join(
                verificationDb.GuildRaidSyncStates,
                guild => guild.Id,
                state => state.GuildId,
                (_, state) => state.ActiveSeasonId)
            .SingleAsync(TestContext.Current.CancellationToken);
        var repaired = await verificationDb.GuildRaidAttacks.SingleAsync(
            attack => attack.GuildRaidSeasonId == verificationSeasonId,
            TestContext.Current.CancellationToken);
        Assert.Equal(first.Season.Boss.ProgressionIndex, repaired.ProgressionIndex);
    }

    [Fact]
    public async Task NoActiveSeasonIsSuccessfulAndReadsReuseItWithoutUpstreamCalls()
    {
        var ready = await RegisterAsync();
        FakeTacticusApi.ConfigureGuildRaidResponse(ready.Token, new GuildRaidResponse());

        var refreshed = await PostRefreshAsync(ready.Client);
        var first = await ready.Client.GetFromJsonAsync<GuildRaidStatusResponse>(
            StatusPath, TestContext.Current.CancellationToken);

        Assert.NotNull(refreshed);
        Assert.NotNull(first);
        Assert.Equal(GuildRaidObservationState.NoActiveSeason, refreshed.State);
        Assert.Null(first.Season);
        Assert.Equal(refreshed.ObservedAt, first.ObservedAt);
        Assert.Equal(1, FakeTacticusApi.GuildRaidCallCount(ready.Token));
    }

    [Fact]
    public async Task RefreshWithinCooldownReturnsPersistedResultWithoutCallingUpstreamAgain()
    {
        var ready = await RegisterAsync();
        FakeTacticusApi.ConfigureGuildRaidResponse(ready.Token, BuildActiveResponse());

        var first = await PostRefreshAsync(ready.Client);
        var second = await PostRefreshAsync(ready.Client);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.ObservedAt, second.ObservedAt);
        Assert.Equal(GuildRaidFreshness.Fresh, second.Freshness);
        Assert.Equal(1, FakeTacticusApi.GuildRaidCallCount(ready.Token));
    }

    [Fact]
    public async Task RefreshAfterCooldownExpiresCallsUpstreamAgain()
    {
        var ready = await RegisterAsync();
        FakeTacticusApi.ConfigureGuildRaidResponse(ready.Token, BuildActiveResponse());
        await PostRefreshAsync(ready.Client);
        await ExpireCooldownAsync(ready.Tag);

        await PostRefreshAsync(ready.Client);

        Assert.Equal(2, FakeTacticusApi.GuildRaidCallCount(ready.Token));
    }

    [Fact]
    public async Task TransientFailureAfterCooldownReturnsPersistedStaleOnBothRefreshAndRead()
    {
        var ready = await RegisterAsync();
        FakeTacticusApi.ConfigureGuildRaidResponse(ready.Token, BuildActiveResponse());
        await PostRefreshAsync(ready.Client);
        await ExpireCooldownAsync(ready.Tag);
        FakeTacticusApi.ConfigureGuildRaidUnavailable(ready.Token);

        var stale = await PostRefreshAsync(ready.Client);
        Assert.NotNull(stale);
        Assert.Equal(GuildRaidFreshness.Stale, stale.Freshness);

        var read = await ready.Client.GetFromJsonAsync<GuildRaidStatusResponse>(
            StatusPath, TestContext.Current.CancellationToken);
        Assert.NotNull(read);
        Assert.Equal(GuildRaidFreshness.Stale, read.Freshness);
        Assert.Equal(stale.ObservedAt, read.ObservedAt);
    }

    [Fact]
    public async Task RejectedCredentialAfterCooldownReturnsBadGatewayAndDoesNotServeStale()
    {
        var ready = await RegisterAsync();
        FakeTacticusApi.ConfigureGuildRaidResponse(ready.Token, BuildActiveResponse());
        await PostRefreshAsync(ready.Client);
        await ExpireCooldownAsync(ready.Tag);
        FakeTacticusApi.ConfigureGuildRaidRejection(ready.Token, HttpStatusCode.Unauthorized);

        Assert.Equal(HttpStatusCode.BadGateway, (await ready.Client.PostAsync(
            RefreshPath, null, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task RetainedObservationThatNoLongerResolvesInCatalogReturnsBadGatewayOnRead()
    {
        var ready = await RegisterAsync();
        FakeTacticusApi.ConfigureGuildRaidResponse(ready.Token, BuildActiveResponse());
        await PostRefreshAsync(ready.Client);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
            var guildId = await db.Guilds.Where(guild => guild.Tag == ready.Tag)
                .Select(guild => guild.Id)
                .SingleAsync(TestContext.Current.CancellationToken);
            var season = await db.GuildRaidSeasons.SingleAsync(
                entity => entity.GuildId == guildId, TestContext.Current.CancellationToken);
            season.SeasonConfigId = "unknown-season-config";
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(HttpStatusCode.BadGateway, (await ready.Client.GetAsync(
            StatusPath, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task TransientFailureWithoutRetainedObservationIsUnavailable()
    {
        var ready = await RegisterAsync();
        FakeTacticusApi.ConfigureGuildRaidUnavailable(ready.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await ready.Client.PostAsync(
            RefreshPath, null, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task OverlappingForcedRefreshesShareOneUpstreamOperation()
    {
        var ready = await RegisterAsync();
        FakeTacticusApi.ConfigureGuildRaidResponse(ready.Token, BuildActiveResponse());
        var gate = FakeTacticusApi.ConfigureGuildRaidGate(ready.Token);

        var first = ready.Client.PostAsync(RefreshPath, null, TestContext.Current.CancellationToken);
        var second = ready.Client.PostAsync(RefreshPath, null, TestContext.Current.CancellationToken);
        await WaitForCallAsync(ready.Token);
        Assert.Equal(1, FakeTacticusApi.GuildRaidCallCount(ready.Token));
        gate.SetResult();

        (await first).EnsureSuccessStatusCode();
        (await second).EnsureSuccessStatusCode();
        Assert.Equal(1, FakeTacticusApi.GuildRaidCallCount(ready.Token));
    }

    [Fact]
    public async Task RefreshArrivingWhileAnotherIsInFlightJoinsItInsteadOfReadingAStaleCooldownResult()
    {
        var ready = await RegisterAsync();
        FakeTacticusApi.ConfigureGuildRaidResponse(ready.Token, BuildActiveResponse());
        var initial = await PostRefreshAsync(ready.Client);
        await ExpireCooldownAsync(ready.Tag);

        var gate = FakeTacticusApi.ConfigureGuildRaidGate(ready.Token);
        var first = ready.Client.PostAsync(RefreshPath, null, TestContext.Current.CancellationToken);
        // The first call already recorded its attempt (opening a fresh cooldown window) and is now
        // blocked in-flight on the gate; a second refresh arriving now must join that flight rather than
        // reading the just-updated cooldown state and returning the old observation as an instant "stale"
        // result without ever calling upstream again.
        await WaitForCallCountAsync(ready.Token, 2);

        var second = ready.Client.PostAsync(RefreshPath, null, TestContext.Current.CancellationToken);
        gate.SetResult();

        var firstResponse = await (await first).Content.ReadFromJsonAsync<GuildRaidStatusResponse>(
            TestContext.Current.CancellationToken);
        var secondResponse = await (await second).Content.ReadFromJsonAsync<GuildRaidStatusResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(initial);
        Assert.NotNull(firstResponse);
        Assert.NotNull(secondResponse);
        Assert.Equal(2, FakeTacticusApi.GuildRaidCallCount(ready.Token));
        Assert.Equal(GuildRaidFreshness.Fresh, secondResponse.Freshness);
        Assert.Equal(firstResponse.ObservedAt, secondResponse.ObservedAt);
        Assert.True(secondResponse.ObservedAt > initial.ObservedAt);
    }

    [Fact]
    public async Task CancellingOneRefreshRequestDoesNotAffectAnotherCallersJoinedFlight()
    {
        var ready = await RegisterAsync();
        FakeTacticusApi.ConfigureGuildRaidResponse(ready.Token, BuildActiveResponse());
        var gate = FakeTacticusApi.ConfigureGuildRaidGate(ready.Token);

        using var cancelledRequestCts = new CancellationTokenSource();
        var cancelled = ready.Client.PostAsync(RefreshPath, null, cancelledRequestCts.Token);
        await WaitForCallAsync(ready.Token);

        var survivor = ready.Client.PostAsync(RefreshPath, null, TestContext.Current.CancellationToken);
        await cancelledRequestCts.CancelAsync();
        try
        {
            await cancelled;
        }
        catch (Exception)
        {
            // Expected: only the cancelled caller's own wait is aborted. The shared flight, and every
            // other caller joined to it, must be unaffected by this caller's cancellation.
        }

        gate.SetResult();

        (await survivor).EnsureSuccessStatusCode();
        Assert.Equal(1, FakeTacticusApi.GuildRaidCallCount(ready.Token));
    }

    private async Task<ReadyGuild> RegisterAsync()
    {
        var (client, tacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var token = $"guild-raid-token-{Guid.NewGuid()}";
        var tag = $"R{Guid.NewGuid():N}"[..10];
        FakeTacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(
                Guid.NewGuid(), tag, "Raid Guild", 10,
                (tacticusUserId, GuildRole.LEADER, 50, null)));
        var response = await client.PostAsJsonAsync(
            "/api/v1/guilds/register",
            new RegisterGuildRequest(token),
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return new ReadyGuild(client, token, tag);
    }

    private static async Task<GuildRaidStatusResponse?> PostRefreshAsync(HttpClient client)
    {
        var response = await client.PostAsync(RefreshPath, null, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GuildRaidStatusResponse>(TestContext.Current.CancellationToken);
    }

    private async Task ExpireCooldownAsync(string tag)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var guildId = await db.Guilds.Where(guild => guild.Tag == tag)
            .Select(guild => guild.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
        var state = await db.GuildRaidSyncStates.SingleAsync(
            entity => entity.GuildId == guildId, TestContext.Current.CancellationToken);
        state.LastAttemptedAt = DateTimeOffset.UtcNow - GuildRaidStatusService.CooldownWindow - TimeSpan.FromSeconds(1);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private GuildRaidResponse BuildActiveResponse(
        bool includeProgressionSuffix = true,
        bool useAdvancedProgression = false)
    {
        var catalog = factory.Services.GetRequiredService<IGameCatalogProvider>().Current;
        var config = catalog.RaidBossesView.Seasons.OrderBy(pair => pair.Key, StringComparer.Ordinal).First();
        var tier = config.Value.Tiers[0];
        var set = tier.Sets[0];
        var boss = set.Encounters.First(encounter => encounter.EncounterType == "Boss");
        if (useAdvancedProgression)
        {
            var advanced = config.Value.Tiers
                .SelectMany(candidateTier => candidateTier.Sets.Select(candidateSet => new
                {
                    Tier = candidateTier,
                    Set = candidateSet,
                    Boss = candidateSet.Encounters.First(encounter => encounter.EncounterType == "Boss"),
                }))
                .First(candidate => candidate.Boss.ProgressionIndex > 1);
            tier = advanced.Tier;
            set = advanced.Set;
            boss = advanced.Boss;
        }
        var maximumHp = catalog.RaidBossesView.Bosses
            .Single(unit => unit.UnitSetId == boss.UnitSetId)
            .StatProgression[boss.ProgressionIndex - 1].Health;
        return new GuildRaidResponse
        {
            Season = 77,
            SeasonConfigId = config.Key,
            Entries =
            [
                new GuildRaidEntry
                {
                    UserId = Guid.NewGuid(),
                    Tier = tier.Tier,
                    Set = set.Set,
                    EncounterIndex = boss.EncounterIndex,
                    RemainingHp = maximumHp / 2,
                    MaxHp = maximumHp,
                    EncounterType = EncounterType.Boss,
                    UnitId = includeProgressionSuffix
                        ? $"{boss.UnitSetId}:{boss.ProgressionIndex}"
                        : boss.UnitSetId,
                    Rarity = Rarity.Common,
                    DamageDealt = maximumHp / 2,
                    DamageType = DamageType.Battle,
                    CompletedOn = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    GlobalConfigHash = "test-config",
                },
            ],
        };
    }

    private static Task WaitForCallAsync(string token) => WaitForCallCountAsync(token, 1);

    private static async Task WaitForCallCountAsync(string token, int expectedAtLeast)
    {
        for (var attempt = 0; attempt < 100 && FakeTacticusApi.GuildRaidCallCount(token) < expectedAtLeast; attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static Task<string> JsonContent(GuildRaidStatusResponse response) =>
        Task.FromResult(System.Text.Json.JsonSerializer.Serialize(response, System.Text.Json.JsonSerializerOptions.Web));

    private sealed record ReadyGuild(HttpClient Client, string Token, string Tag);
}
