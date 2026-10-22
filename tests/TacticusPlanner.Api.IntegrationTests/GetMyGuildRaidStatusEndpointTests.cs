using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.Domain.GuildRaids;
using TacticusPlanner.Domain.GuildRaids.Enums;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.Persistence;
using TacticusPlanner.TacticusApi.Models.Guild;
using TacticusPlanner.TacticusApi.Models.GuildRaid;
using TacticusPlanner.TacticusApi.Models.Shared;

namespace TacticusPlanner.Api.IntegrationTests;

public sealed class GetMyGuildRaidStatusEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string StatusPath = "/api/v1/guild/raid-status";
    private const string RefreshPath = "/api/v1/guild/raid-status/refresh";

    [Fact]
    public async Task GuildPersistenceRollsBackAfterSqlWasWritten()
    {
        var ct = TestContext.Current.CancellationToken;
        var ready = await RegisterAsync();
        factory.TacticusApi.ConfigureGuildRaidResponse(ready.Token, BuildActiveResponse());
        await using var beforeScope = factory.Services.CreateAsyncScope();
        var beforeDb = beforeScope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var seasons = await beforeDb.GuildRaidSeasons.CountAsync(ct);
        var attacks = await beforeDb.GuildRaidAttacks.CountAsync(ct);
        factory.FailureInjected = false;
        factory.FailAfterSave = db => db.ChangeTracker.Entries<TacticusPlanner.Domain.GuildRaids.GuildRaidSeason>().Any();
        try
        {
            var response = await ready.Client.PostAsync(RefreshPath, null, ct);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.True(factory.FailureInjected);
            await using var afterScope = factory.Services.CreateAsyncScope();
            var db = afterScope.ServiceProvider.GetRequiredService<PlannerDbContext>();
            Assert.Equal(seasons, await db.GuildRaidSeasons.CountAsync(ct));
            Assert.Equal(attacks, await db.GuildRaidAttacks.CountAsync(ct));
            var guildId = await db.Guilds.Where(guild => guild.Tag == ready.Tag).Select(guild => guild.Id).SingleAsync(ct);
            var state = await db.GuildRaidSyncStates.SingleAsync(state => state.GuildId == guildId, ct);
            Assert.Null(state.ActiveSeasonId);
            Assert.Null(state.ObservedAt);
        }
        finally
        {
            factory.FailAfterSave = null;
        }
    }

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
        Assert.Equal(0, factory.TacticusApi.GuildRaidCallCount(ready.Token));

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
        Assert.Equal(0, factory.TacticusApi.GuildRaidCallCount(ready.Token));
    }

    [Fact]
    public async Task ReadyGuildWithNoObservationYetConflictsOnReadWithoutUpstreamCall()
    {
        var ready = await RegisterAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await ready.Client.GetAsync(
            StatusPath, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(0, factory.TacticusApi.GuildRaidCallCount(ready.Token));
    }

    [Fact]
    public async Task ForcedRefreshPersistsAndSubsequentReadsNeverCallUpstream()
    {
        var ready = await RegisterAsync();
        factory.TacticusApi.ConfigureGuildRaidResponse(ready.Token, BuildActiveResponse());

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
        Assert.Equal(1, factory.TacticusApi.GuildRaidCallCount(ready.Token));
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
        factory.TacticusApi.ConfigureGuildRaidResponse(ready.Token, upstream);

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
        factory.TacticusApi.ConfigureGuildRaidResponse(ready.Token, new GuildRaidResponse());

        var refreshed = await PostRefreshAsync(ready.Client);
        var first = await ready.Client.GetFromJsonAsync<GuildRaidStatusResponse>(
            StatusPath, TestContext.Current.CancellationToken);

        Assert.NotNull(refreshed);
        Assert.NotNull(first);
        Assert.Equal(GuildRaidObservationState.NoActiveSeason, refreshed.State);
        Assert.Null(first.Season);
        Assert.Equal(refreshed.ObservedAt, first.ObservedAt);
        Assert.Equal(1, factory.TacticusApi.GuildRaidCallCount(ready.Token));
    }

    [Fact]
    public async Task RefreshWithinCooldownReturnsPersistedResultWithoutCallingUpstreamAgain()
    {
        var ready = await RegisterAsync();
        factory.TacticusApi.ConfigureGuildRaidResponse(ready.Token, BuildActiveResponse());

        var first = await PostRefreshAsync(ready.Client);
        var second = await PostRefreshAsync(ready.Client);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.ObservedAt, second.ObservedAt);
        Assert.Equal(GuildRaidFreshness.Fresh, second.Freshness);
        Assert.Equal(1, factory.TacticusApi.GuildRaidCallCount(ready.Token));
    }

    [Fact]
    public async Task RefreshAfterCooldownExpiresCallsUpstreamAgain()
    {
        var ready = await RegisterAsync();
        factory.TacticusApi.ConfigureGuildRaidResponse(ready.Token, BuildActiveResponse());
        await PostRefreshAsync(ready.Client);
        await ExpireCooldownAsync(ready.Tag);

        await PostRefreshAsync(ready.Client);

        Assert.Equal(2, factory.TacticusApi.GuildRaidCallCount(ready.Token));
    }

    [Fact]
    public async Task TransientFailureAfterCooldownReturnsPersistedStaleOnBothRefreshAndRead()
    {
        var ready = await RegisterAsync();
        factory.TacticusApi.ConfigureGuildRaidResponse(ready.Token, BuildActiveResponse());
        await PostRefreshAsync(ready.Client);
        await ExpireCooldownAsync(ready.Tag);
        factory.TacticusApi.ConfigureGuildRaidUnavailable(ready.Token);

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
        factory.TacticusApi.ConfigureGuildRaidResponse(ready.Token, BuildActiveResponse());
        await PostRefreshAsync(ready.Client);
        await ExpireCooldownAsync(ready.Tag);
        factory.TacticusApi.ConfigureGuildRaidRejection(ready.Token, HttpStatusCode.Unauthorized);

        Assert.Equal(HttpStatusCode.BadGateway, (await ready.Client.PostAsync(
            RefreshPath, null, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task RetainedObservationThatNoLongerResolvesInCatalogReturnsBadGatewayOnRead()
    {
        var ready = await RegisterAsync();
        factory.TacticusApi.ConfigureGuildRaidResponse(ready.Token, BuildActiveResponse());
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
        factory.TacticusApi.ConfigureGuildRaidUnavailable(ready.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await ready.Client.PostAsync(
            RefreshPath, null, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task OverlappingForcedRefreshesShareOneUpstreamOperation()
    {
        var ready = await RegisterAsync();
        factory.TacticusApi.ConfigureGuildRaidResponse(ready.Token, BuildActiveResponse());
        var gate = factory.TacticusApi.ConfigureGuildRaidGate(ready.Token);

        var first = ready.Client.PostAsync(RefreshPath, null, TestContext.Current.CancellationToken);
        var second = ready.Client.PostAsync(RefreshPath, null, TestContext.Current.CancellationToken);
        await WaitForCallAsync(ready.Token);
        Assert.Equal(1, factory.TacticusApi.GuildRaidCallCount(ready.Token));
        gate.SetResult();

        (await first).EnsureSuccessStatusCode();
        (await second).EnsureSuccessStatusCode();
        Assert.Equal(1, factory.TacticusApi.GuildRaidCallCount(ready.Token));
    }

    [Fact]
    public async Task RefreshArrivingWhileAnotherIsInFlightJoinsItInsteadOfReadingAStaleCooldownResult()
    {
        var ready = await RegisterAsync();
        factory.TacticusApi.ConfigureGuildRaidResponse(ready.Token, BuildActiveResponse());
        var initial = await PostRefreshAsync(ready.Client);
        await ExpireCooldownAsync(ready.Tag);

        var gate = factory.TacticusApi.ConfigureGuildRaidGate(ready.Token);
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
        Assert.Equal(2, factory.TacticusApi.GuildRaidCallCount(ready.Token));
        Assert.Equal(GuildRaidFreshness.Fresh, secondResponse.Freshness);
        Assert.Equal(firstResponse.ObservedAt, secondResponse.ObservedAt);
        Assert.True(secondResponse.ObservedAt > initial.ObservedAt);
    }

    [Fact]
    public async Task CancellingOneRefreshRequestDoesNotAffectAnotherCallersJoinedFlight()
    {
        var ready = await RegisterAsync();
        factory.TacticusApi.ConfigureGuildRaidResponse(ready.Token, BuildActiveResponse());
        var gate = factory.TacticusApi.ConfigureGuildRaidGate(ready.Token);

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
        Assert.Equal(1, factory.TacticusApi.GuildRaidCallCount(ready.Token));
    }

    [Fact]
    public async Task BoundedLoadEqualsTheFullSeasonProjectionForAThousandAttacks()
    {
        var ct = TestContext.Current.CancellationToken;
        var ready = await RegisterAsync();
        var catalog = factory.Services.GetRequiredService<IGameCatalogProvider>().Current;
        var config = catalog.RaidBossesView.Seasons.OrderBy(pair => pair.Key, StringComparer.Ordinal).First();
        var positions = config.Value.Tiers.SelectMany(tier => tier.Sets.Select(set => (Tier: tier.Tier, Set: set))).ToList();
        var currentIndex = positions.FindIndex(1, position => position.Set.Encounters.Count(e => e.EncounterType != "Boss") >= 2);
        Assert.True(currentIndex > 0, "The season config needs a later set with two side encounters.");
        var previous = positions[currentIndex - 1];
        var current = positions[currentIndex];
        var boss = current.Set.Encounters.First(encounter => encounter.EncounterType == "Boss");
        var sides = current.Set.Encounters.Where(encounter => encounter.EncounterType != "Boss").OrderBy(e => e.EncounterIndex).ToList();
        var origin = DateTimeOffset.UtcNow.AddHours(-2);
        var attacks = new List<GuildRaidAttack>();

        GuildRaidAttack Attack(
            (int Tier, GameCatalogRaidBossSetView Set) position,
            GameCatalogRaidBossEncounterView encounter,
            int remaining,
            int maximum,
            TimeSpan at) => new()
            {
                Id = GuildRaidAttackId.From(Guid.CreateVersion7()),
                ContentHash = Guid.NewGuid().ToString("N"),
                TacticusUserIdHash = TacticusUserIdHash.From(new byte[32]),
                Tier = position.Tier,
                Set = position.Set.Set,
                EncounterIndex = encounter.EncounterIndex,
                RemainingHp = remaining,
                MaximumHp = maximum,
                EncounterType = encounter.EncounterType == "Boss" ? GuildRaidEncounterType.Boss : GuildRaidEncounterType.SideBoss,
                UnitSetId = encounter.UnitSetId,
                ProgressionIndex = encounter.ProgressionIndex,
                Difficulty = GuildRaidDifficulty.Common,
                DamageDealt = 1,
                DamageType = GuildRaidDamageType.Battle,
                CompletedAt = origin + at,
            };

        // The previous set's boss is defeated first; everything before that kill must stay out of the projection.
        var previousBoss = previous.Set.Encounters.First(encounter => encounter.EncounterType == "Boss");
        attacks.Add(Attack(previous, previousBoss, 0, 1000, TimeSpan.Zero));
        foreach (var prime in previous.Set.Encounters.Where(encounter => encounter.EncounterType != "Boss"))
        {
            attacks.Add(Attack(previous, prime, 7, 1000, TimeSpan.FromSeconds(-30)));
        }

        // A sparse side encounter hit once, immediately after the kill; then a busy boss and a busy side encounter.
        attacks.Add(Attack(current, sides[1], 400, 1000, TimeSpan.FromSeconds(1)));
        for (var index = 0; index < 400; index++)
        {
            attacks.Add(Attack(current, boss, 1000 - index - 1, 1000, TimeSpan.FromSeconds(10 + index)));
        }

        for (var index = 0; attacks.Count < 1000; index++)
        {
            attacks.Add(Attack(current, sides[0], 900 - index, 1000, TimeSpan.FromSeconds(500 + index)));
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
            var guildId = await db.Guilds.Where(guild => guild.Tag == ready.Tag).Select(guild => guild.Id).SingleAsync(ct);
            var observed = DateTimeOffset.UtcNow;
            var season = new GuildRaidSeason
            {
                Id = GuildRaidSeasonId.From(Guid.CreateVersion7()),
                GuildId = guildId,
                SeasonNumber = 77,
                SeasonConfigId = config.Key,
                ObservedAt = observed,
            };
            foreach (var attack in attacks)
            {
                season.Attacks.Add(attack);
            }

            db.GuildRaidSeasons.Add(season);
            db.GuildRaidSyncStates.Add(new GuildRaidSyncState
            {
                GuildId = guildId,
                State = GuildRaidObservationState.Active,
                ObservedAt = observed,
                LastAttemptedAt = observed,
                ActiveSeasonId = season.Id,
            });
            await db.SaveChangesAsync(ct);
        }

        var response = await ready.Client.GetAsync(StatusPath, ct);
        response.EnsureSuccessStatusCode();
        var actual = await response.Content.ReadFromJsonAsync<GuildRaidStatusResponse>(ct);

        await using var verification = factory.Services.CreateAsyncScope();
        var verificationDb = verification.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var state = await verificationDb.GuildRaidSyncStates.AsNoTracking()
            .SingleAsync(entity => entity.Guild!.Tag == ready.Tag, ct);
        var full = await verificationDb.GuildRaidSeasons.AsNoTracking().Include(entity => entity.Attacks)
            .SingleAsync(entity => entity.Id == state.ActiveSeasonId, ct);
        Assert.Equal(1000, full.Attacks.Count);
        full.Attacks = [.. full.Attacks.OrderBy(attack => attack.CompletedAt).ThenBy(attack => attack.Id)];
        var guild = await verificationDb.Guilds.AsNoTracking().SingleAsync(entity => entity.Tag == ready.Tag, ct);
        var expected = GuildRaidStatusProjector.Project(
            state, full, catalog, guild.LastSyncSucceededAt!.Value, GuildRaidFreshness.Fresh);

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(expected, System.Text.Json.JsonSerializerOptions.Web),
            System.Text.Json.JsonSerializer.Serialize(actual, System.Text.Json.JsonSerializerOptions.Web));
        // The projection is non-trivial: the position advanced past the kill, the sparse side encounter kept
        // its single observation and the busy one its latest.
        Assert.Equal(boss.UnitSetId, actual!.Season!.Boss.UnitSetId);
        Assert.Equal(400, actual.Season.Primes.Single(prime => prime.EncounterIndex == sides[1].EncounterIndex).RemainingHp);
        // Every read of the 1000 attacks is bounded: a single-row lookup or one latest row per encounter.
        var attackReads = factory.CommandTextsFor(response).Where(text => text.Contains("FROM guild_raid_attacks", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, attackReads.Count);
        Assert.All(attackReads, text => Assert.True(
            text.Contains("LIMIT 1", StringComparison.Ordinal) || text.Contains("row <= 1", StringComparison.Ordinal), text));
        Assert.True(factory.CommandsFor(response) <= 7, $"{factory.CommandsFor(response)} commands");
    }

    private async Task<ReadyGuild> RegisterAsync()
    {
        var (client, tacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var token = $"guild-raid-token-{Guid.NewGuid()}";
        var tag = $"R{Guid.NewGuid():N}"[..10];
        factory.TacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(
                Guid.NewGuid(), tag, "Raid Guild", 10,
                (tacticusUserId, GuildRole.LEADER, 50, null)));
        var response = await client.PostAsJsonAsync(
            "/api/v1/guild/register",
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

    private Task WaitForCallAsync(string token) => WaitForCallCountAsync(token, 1);

    private async Task WaitForCallCountAsync(string token, int expectedAtLeast)
    {
        for (var attempt = 0; attempt < 100 && factory.TacticusApi.GuildRaidCallCount(token) < expectedAtLeast; attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static Task<string> JsonContent(GuildRaidStatusResponse response) =>
        Task.FromResult(System.Text.Json.JsonSerializer.Serialize(response, System.Text.Json.JsonSerializerOptions.Web));

    private sealed record ReadyGuild(HttpClient Client, string Token, string Tag);
}
