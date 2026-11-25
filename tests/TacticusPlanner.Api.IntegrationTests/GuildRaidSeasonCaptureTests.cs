using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.Domain.GuildRaids;
using TacticusPlanner.Domain.GuildRaids.Enums;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.Persistence;
using TacticusPlanner.TacticusApi;
using TacticusPlanner.TacticusApi.Models.GuildRaid;
using TacticusPlanner.TacticusApi.Models.Shared;
using UpstreamGuildRole = TacticusPlanner.TacticusApi.Models.Guild.GuildRole;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>Capture runs driven directly, so each test owns its timing; the worker is off in this factory.</summary>
public sealed class GuildRaidSeasonCaptureTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string StatusPath = "/api/v1/guild/raid-status";
    private const string RefreshPath = "/api/v1/guild/raid-status/refresh";
    private const string SeasonsPath = "/api/v1/guild/raid/seasons";
    private const int Active = 114;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BackfillCapturesEveryPastSeasonInOrderAndLeavesLiveStatusAlone()
    {
        var guild = await ActiveGuildAsync(104);
        foreach (var season in Enumerable.Range(104, 10))
        {
            factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(season, Entry(), Entry()));
        }

        var before = await guild.Client.GetStringAsync(StatusPath, Ct);
        await RunAsync(guild);

        Assert.Equal(Enumerable.Range(104, 10), factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));
        Assert.Equal(before, await guild.Client.GetStringAsync(StatusPath, Ct));
        await using var db = Db();
        var captures = await CapturesAsync(db, guild);
        Assert.All(captures, capture => Assert.Equal(GuildRaidSeasonCaptureStatus.Final, capture.Status));
        Assert.Equal(20, await db.GuildRaidAttacks.CountAsync(
            attack => attack.Season!.GuildId == guild.Id && attack.Season.SeasonNumber < Active, Ct));
        var state = await StateAsync(db, guild);
        Assert.Equal(Active, state.SeasonsPlannedFor);
        Assert.Null(state.CaptureNotBefore);

        // The production default, which the runs of this class narrow.
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(70, scope.ServiceProvider.GetRequiredService<GuildRaidSeasonCaptureService>().FirstSeason);

        // Nothing is due any more, so neither the guild nor a season is asked for again.
        var guildCalls = factory.TacticusApi.GuildCallCount(guild.Token);
        await RunAsync(guild);
        Assert.Equal(guildCalls, factory.TacticusApi.GuildCallCount(guild.Token));
        Assert.Equal(10, factory.TacticusApi.GuildRaidSeasonCalls(guild.Token).Count);
    }

    [Fact]
    public async Task InterruptedBackfillResumesWithTheRemainingSeasonsOnceItsLeaseExpires()
    {
        var guild = await ActiveGuildAsync(104);
        using var host = new CancellationTokenSource();
        foreach (var season in Enumerable.Range(104, 10))
        {
            factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(season, Entry()));
        }

        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, 107, async () =>
        {
            await host.CancelAsync();
            throw new OperationCanceledException(host.Token);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(guild, host.Token));

        // The lease is still held, so nothing starts yet.
        await RunAsync(guild);
        Assert.Equal([104, 105, 106, 107], factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));

        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(107, Entry()));
        await SetNotBeforeAsync(guild, DateTimeOffset.UtcNow.AddSeconds(-1));
        await RunAsync(guild);
        Assert.Equal(Enumerable.Range(107, 7), factory.TacticusApi.GuildRaidSeasonCalls(guild.Token).Skip(4));
    }

    [Fact]
    public async Task ConcurrentRunsClaimTheGuildOnce()
    {
        var guild = await ActiveGuildAsync(113);
        var gate = new TaskCompletionSource<GuildRaidResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, 113, () => gate.Task);
        var guildCalls = factory.TacticusApi.GuildCallCount(guild.Token);

        var runs = Enumerable.Range(0, 4).Select(_ => RunAsync(guild)).ToArray();
        await Task.WhenAny(Task.WhenAll(runs), Task.Delay(TimeSpan.FromSeconds(1), Ct));
        gate.SetResult(Season(113, Entry()));
        await Task.WhenAll(runs);

        Assert.Equal(guildCalls + 1, factory.TacticusApi.GuildCallCount(guild.Token));
        Assert.Single(factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));
    }

    [Fact]
    public async Task RunThatLostItsLeaseStopsAndKeepsTheNewOne()
    {
        var guild = await ActiveGuildAsync(112);
        var other = new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, 112, async () =>
        {
            await SetNotBeforeAsync(guild, other);
            return Season(112, Entry());
        });

        await RunAsync(guild);

        Assert.Equal([112], factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));
        await using var db = Db();
        Assert.Equal(other, (await StateAsync(db, guild)).CaptureNotBefore);
    }

    [Fact]
    public async Task LiveRefreshDuringARunKeepsItsLease()
    {
        var guild = await ActiveGuildAsync(112);
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, 112, async () =>
        {
            await ExpireRefreshCooldownAsync(guild);
            (await guild.Client.PostAsync(RefreshPath, null, Ct)).EnsureSuccessStatusCode();
            return Season(112, Entry());
        });
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(113, Entry()));

        await RunAsync(guild);

        Assert.Equal([112, 113], factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));
    }

    [Fact]
    public async Task KeyOfAnotherGuildStoresNothing()
    {
        var guild = await ActiveGuildAsync(113);
        factory.TacticusApi.ConfigureGuildResponse(guild.Token, Upstream(Guid.NewGuid(), guild));

        await RunAsync(guild);

        Assert.Empty(factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));
        await using var db = Db();
        Assert.Empty(await CapturesAsync(db, guild));
        Assert.Null((await StateAsync(db, guild)).SeasonsPlannedFor);
    }

    [Fact]
    public async Task KeyIsCheckedAgainWhenOnlyPendingSeasonsAreLeft()
    {
        var guild = await ActiveGuildAsync(113);
        factory.TacticusApi.ConfigureGuildRaidSeasonRejection(guild.Token, 113, HttpStatusCode.InternalServerError);
        await RunAsync(guild);
        factory.TacticusApi.ConfigureGuildResponse(guild.Token, Upstream(Guid.NewGuid(), guild));
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(113, Entry()));

        await RunAgainAsync(guild);

        Assert.Single(factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));
        Assert.Equal(GuildRaidSeasonCaptureStatus.Pending, await StatusOfAsync(guild, 113));
    }

    [Fact]
    public async Task SeasonsTacticusDoesNotListAreCapturedWithoutTouchingMembers()
    {
        var guild = await ActiveGuildAsync(112);
        await StoreSeasonAsync(guild, 103, Entry());
        factory.TacticusApi.ConfigureGuildResponse(
            guild.Token, Upstream(guild.TacticusGuildId, guild, UpstreamGuildRole.MEMBER, listed: [Active]));
        foreach (var season in new[] { 103, 112, 113 })
        {
            factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(season, Entry()));
        }

        await RunAsync(guild);

        Assert.Equal([103, 112, 113], factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));
        await using var db = Db();
        Assert.All(await CapturesAsync(db, guild), capture => Assert.Equal(GuildRaidSeasonCaptureStatus.Final, capture.Status));
        Assert.Equal(2, await HitsAsync(db, guild, 103));
        Assert.Equal(GuildRole.Leader, (await db.GuildMembers.SingleAsync(member => member.GuildId == guild.Id, Ct)).Role);
    }

    [Fact]
    public async Task NewActiveSeasonRequestsOnlyTheSeasonThatEnded()
    {
        var guild = await ActiveGuildAsync(112);
        foreach (var season in new[] { 112, 113, Active })
        {
            factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(season, Entry()));
        }

        await RunAsync(guild);
        await ActivateAsync(guild, Active + 1);
        await RunAsync(guild);

        Assert.Equal([112, 113, Active], factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));
    }

    [Fact]
    public async Task PastHitsAreStoredFromTheirOwnDataWhateverTodaysCatalogSays()
    {
        var guild = await ActiveGuildAsync(113);
        var suffixed = Entry();
        suffixed.UnitId = "guildBossRetired:3";
        suffixed.Tier = 7;
        var plain = Entry();
        plain.UnitId = "guildBossRetired";
        var response = Season(113, suffixed, plain);
        response.SeasonConfigId = "config-the-catalog-never-had";
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, response);

        await RunAsync(guild);

        await using var db = Db();
        var hits = await db.GuildRaidAttacks.Where(attack => attack.Season!.GuildId == guild.Id
            && attack.Season.SeasonNumber == 113).ToListAsync(Ct);
        Assert.All(hits, hit => Assert.Equal("guildBossRetired", hit.UnitSetId));
        var withSuffix = Assert.Single(hits, hit => hit.ProgressionIndex == 3);
        Assert.Equal(7, withSuffix.Tier);
        Assert.Equal(suffixed.UserId.ToString()[^4..], withSuffix.TacticusUserIdSuffix);
        Assert.Equal("ultraTigurius", Assert.Single(withSuffix.Units).UnitId);
        Assert.Single(hits, hit => hit.ProgressionIndex is null);
        Assert.Equal(GuildRaidSeasonCaptureStatus.Final, await StatusOfAsync(guild, 113));
    }

    [Fact]
    public async Task MalformedEntriesAreSkippedAndCounted()
    {
        var guild = await ActiveGuildAsync(113);
        var unfinished = Entry();
        unfinished.CompletedOn = null;
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(113, Entry(), unfinished, Entry()));

        await RunAsync(guild);

        await using var db = Db();
        Assert.Equal(2, await HitsAsync(db, guild, 113));
        var capture = Assert.Single(await CapturesAsync(db, guild));
        Assert.Equal((GuildRaidSeasonCaptureStatus.Final, 1), (capture.Status, capture.SkippedEntryCount));
    }

    [Fact]
    public async Task CaptureAddsToLiveHitsWithoutDuplicatingOrDeletingAnyAndFillsTheirSuffix()
    {
        var guild = await ActiveGuildAsync(113);
        var same = Entry();
        var rehashed = Entry();
        var onlyStored = Entry();
        await StoreSeasonAsync(guild, 113, same, rehashed, onlyStored);
        await using (var db = Db())
        {
            await db.GuildRaidAttacks.Where(attack => attack.Season!.GuildId == guild.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(attack => attack.TacticusUserIdSuffix, (string?)null), Ct);
        }

        var again = Copy(rehashed);
        again.GlobalConfigHash = "a later config";
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(113, Copy(same), again, Entry()));
        await RunAsync(guild);

        await using var verify = Db();
        var hits = await verify.GuildRaidAttacks.Where(attack => attack.Season!.GuildId == guild.Id
            && attack.Season.SeasonNumber == 113).ToListAsync(Ct);
        Assert.Equal(4, hits.Count);
        Assert.All([same, rehashed], entry =>
            Assert.Single(hits, hit => hit.TacticusUserIdSuffix == entry.UserId.ToString()[^4..]));
        // The hit the response left out is kept, and nothing says whose it is.
        Assert.Single(hits, hit => hit.TacticusUserIdSuffix is null);
        Assert.Equal(GuildRaidSeasonCaptureStatus.Final, await StatusOfAsync(guild, 113));
    }

    [Fact]
    public async Task EmptySeasonIsFinalOnlyWhenNothingWasSeenOfIt()
    {
        var guild = await ActiveGuildAsync(111);
        await StoreSeasonAsync(guild, 111, Entry());
        var malformed = Entry();
        malformed.UserId = Guid.Empty;
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(111));
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(112, malformed));
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(113));

        await RunAsync(guild);

        Assert.Equal(GuildRaidSeasonCaptureStatus.Pending, await StatusOfAsync(guild, 111));
        Assert.Equal(GuildRaidSeasonCaptureStatus.Pending, await StatusOfAsync(guild, 112));
        Assert.Equal(GuildRaidSeasonCaptureStatus.Final, await StatusOfAsync(guild, 113));
        await using var db = Db();
        Assert.Equal(1, await HitsAsync(db, guild, 111));
        AssertRetryInAnHour(await StateAsync(db, guild));
    }

    [Fact]
    public async Task WrongSeasonNumberStoresNothingAndTheRunContinues()
    {
        var guild = await ActiveGuildAsync(112);
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, 112, () => Task.FromResult(Season(111, Entry())));
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(113, Entry()));

        await RunAsync(guild);

        Assert.Equal(GuildRaidSeasonCaptureStatus.Pending, await StatusOfAsync(guild, 112));
        Assert.Equal(GuildRaidSeasonCaptureStatus.Final, await StatusOfAsync(guild, 113));
        await using var db = Db();
        Assert.Equal(0, await HitsAsync(db, guild, 111) + await HitsAsync(db, guild, 112));
    }

    [Theory]
    [InlineData("400")]
    [InlineData("401")]
    [InlineData("403")]
    [InlineData("500")]
    [InlineData("timeout")]
    public async Task FailureEndsTheRunKeepsWhatWasCapturedAndRetriesAfterAnHour(string failure)
    {
        var guild = await ActiveGuildAsync(111);
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(111, Entry()));
        if (int.TryParse(failure, out var code))
        {
            factory.TacticusApi.ConfigureGuildRaidSeasonRejection(guild.Token, 112, (HttpStatusCode)code);
        }
        else
        {
            factory.TacticusApi.ConfigureGuildRaidSeason(
                guild.Token, 112, () => throw new TaskCanceledException("Simulated timeout."));
        }

        await RunAsync(guild);
        await RunAsync(guild);

        Assert.Equal([111, 112], factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));
        Assert.Equal(GuildRaidSeasonCaptureStatus.Final, await StatusOfAsync(guild, 111));
        Assert.Equal(GuildRaidSeasonCaptureStatus.Pending, await StatusOfAsync(guild, 112));
        Assert.Equal(GuildRaidSeasonCaptureStatus.Pending, await StatusOfAsync(guild, 113));
        await using var db = Db();
        AssertRetryInAnHour(await StateAsync(db, guild));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("empty")]
    [InlineData("rarity")]
    [InlineData("timestamp")]
    [InlineData("units")]
    public async Task UnreadableSeasonStaysPendingWithoutHoldingUpNewerOnes(string defect)
    {
        var guild = await ActiveGuildAsync(112);
        var bad = Entry();
        switch (defect)
        {
            case "json":
                factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, 112, RealClientReadingUnreadableBodyAsync);
                break;
            case "empty":
                factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, 112, () => Task.FromResult<GuildRaidResponse>(null!));
                break;
            case "rarity": bad.Rarity = (Rarity)99; break;
            case "timestamp": bad.CompletedOn = long.MaxValue; break;
            case "units": bad.HeroDetails = null!; break;
        }

        if (defect is not ("json" or "empty"))
        {
            factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(112, Entry(), bad));
        }

        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(113, Entry()));

        await RunAsync(guild);

        Assert.Equal([112, 113], factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));
        Assert.Equal(GuildRaidSeasonCaptureStatus.Pending, await StatusOfAsync(guild, 112));
        Assert.Equal(GuildRaidSeasonCaptureStatus.Final, await StatusOfAsync(guild, 113));
        await using var db = Db();
        Assert.Equal(0, await HitsAsync(db, guild, 112));
        AssertRetryInAnHour(await StateAsync(db, guild));
    }

    // The real client, so the test holds whatever exception Refit raises for a successful but unreadable body.
    private static async Task<GuildRaidResponse> RealClientReadingUnreadableBodyAsync()
    {
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(http => http.AddHttpMessageHandler(() => new UnreadableBodyHandler()));
        services.AddTacticusApi("https://tacticus.test");
        await using var provider = services.BuildServiceProvider();
        return await provider.GetRequiredService<ITacticusApi>().GetGuildRaidBySeasonAsync("token", 112, Ct);
    }

    private sealed class UnreadableBodyHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent("{\"season\":112,\"entries\":[{\"tier\":\"x\"}]}", Encoding.UTF8, "application/json"),
            });
    }

    [Fact]
    public async Task ThirdNotFoundMakesASeasonUnavailableForGood()
    {
        var guild = await ActiveGuildAsync(112);
        factory.TacticusApi.ConfigureGuildRaidSeasonRejection(guild.Token, 112, HttpStatusCode.NotFound);
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(113, Entry()));

        await RunAsync(guild);
        Assert.Equal(GuildRaidSeasonCaptureStatus.Pending, await StatusOfAsync(guild, 112));
        Assert.Equal(GuildRaidSeasonCaptureStatus.Final, await StatusOfAsync(guild, 113));
        await RunAgainAsync(guild);
        Assert.Equal(GuildRaidSeasonCaptureStatus.Pending, await StatusOfAsync(guild, 112));
        await RunAgainAsync(guild);
        Assert.Equal(GuildRaidSeasonCaptureStatus.Unavailable, await StatusOfAsync(guild, 112));

        await ReplanAsync(guild);
        Assert.Equal(GuildRaidSeasonCaptureStatus.Unavailable, await StatusOfAsync(guild, 112));
        Assert.Equal([112, 113, 112, 112], factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));
    }

    [Fact]
    public async Task NoActiveSeasonCapturesNothing()
    {
        var guild = await ActiveGuildAsync(113);
        factory.TacticusApi.ConfigureGuildRaidResponse(guild.Token, new GuildRaidResponse());
        await ExpireRefreshCooldownAsync(guild);
        (await guild.Client.PostAsync(RefreshPath, null, Ct)).EnsureSuccessStatusCode();
        var guildCalls = factory.TacticusApi.GuildCallCount(guild.Token);

        await RunAsync(guild);

        Assert.Equal(guildCalls, factory.TacticusApi.GuildCallCount(guild.Token));
        Assert.Empty(factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));
    }

    [Fact]
    public async Task FullTacticusUserIdIsInNoRaidColumn()
    {
        var guild = await ActiveGuildAsync(113);
        var entry = Entry();
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(113, entry));
        await RunAsync(guild);

        await using var db = Db();
        foreach (var table in new[] { "guild_raid_attacks", "guild_raid_seasons", "guild_raid_season_captures", "guild_raid_sync_states" })
        {
            foreach (var id in new[] { entry.UserId, guild.LiveEntry.UserId })
            {
                // Table names are the constants above.
#pragma warning disable EF1002
                var found = await db.Database
                    .SqlQueryRaw<int>($"SELECT count(*)::int AS \"Value\" FROM {table} t WHERE t::text LIKE {{0}}", $"%{id}%")
                    .SingleAsync(Ct);
#pragma warning restore EF1002
                Assert.Equal(0, found);
            }
        }

        Assert.Equal(
            guild.LiveEntry.UserId.ToString()[^4..],
            await db.GuildRaidAttacks.Where(attack => attack.Season!.GuildId == guild.Id && attack.Season.SeasonNumber == Active)
                .Select(attack => attack.TacticusUserIdSuffix).SingleAsync(Ct));
    }

    [Fact]
    public async Task RefreshFillsTheSuffixOfAHitStoredWithoutIt()
    {
        var guild = await ActiveGuildAsync(Active);
        await using (var db = Db())
        {
            await db.GuildRaidAttacks.Where(attack => attack.Season!.GuildId == guild.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(attack => attack.TacticusUserIdSuffix, (string?)null), Ct);
        }

        await ExpireRefreshCooldownAsync(guild);
        (await guild.Client.PostAsync(RefreshPath, null, Ct)).EnsureSuccessStatusCode();

        await using var verify = Db();
        Assert.Equal(
            guild.LiveEntry.UserId.ToString()[^4..],
            await verify.GuildRaidAttacks.Where(attack => attack.Season!.GuildId == guild.Id)
                .Select(attack => attack.TacticusUserIdSuffix).SingleAsync(Ct));
    }

    [Fact]
    public async Task OnlyAFreshlyPersistedActiveSeasonIsQueued()
    {
        var queue = factory.Services.GetRequiredService<GuildRaidSeasonCaptureRequests>();
        var guild = await ActiveGuildAsync(Active);
        List<GuildId> Drain()
        {
            var queued = new List<GuildId>();
            while (queue.Reader.TryRead(out var id))
            {
                queued.Add(id);
            }

            return [.. queued.Where(id => id == guild.Id)];
        }

        Assert.Single(Drain());

        await guild.Client.PostAsync(RefreshPath, null, Ct); // cooldown
        await guild.Client.GetAsync(StatusPath, Ct);
        await guild.Client.GetAsync(SeasonsPath, Ct);
        Assert.Empty(Drain());

        await ExpireRefreshCooldownAsync(guild);
        factory.TacticusApi.ConfigureGuildRaidResponse(guild.Token, new GuildRaidResponse());
        (await guild.Client.PostAsync(RefreshPath, null, Ct)).EnsureSuccessStatusCode();
        Assert.Empty(Drain());

        await ExpireRefreshCooldownAsync(guild);
        factory.TacticusApi.ConfigureGuildRaidRejection(guild.Token, HttpStatusCode.Forbidden);
        await guild.Client.PostAsync(RefreshPath, null, Ct);
        Assert.Empty(Drain());
    }

    [Fact]
    public async Task SeasonsListsEveryStatusNewestFirstWithoutUpstreamCallsOrPlayerData()
    {
        var guild = await ActiveGuildAsync(111);
        await StoreSeasonAsync(guild, 103, Entry());
        var skipped = Entry();
        skipped.CompletedOn = null;
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(111));
        factory.TacticusApi.ConfigureGuildRaidSeasonRejection(guild.Token, 112, HttpStatusCode.NotFound);
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(113, Entry(), skipped));
        factory.TacticusApi.ConfigureGuildRaidSeasonRejection(guild.Token, 103, HttpStatusCode.NotFound);
        await RunAsync(guild);
        await using (var db = Db())
        {
            await db.GuildRaidSeasonCaptures.Where(capture => capture.GuildId == guild.Id && capture.SeasonNumber == 103)
                .ExecuteUpdateAsync(set => set.SetProperty(capture => capture.Status, GuildRaidSeasonCaptureStatus.Unavailable), Ct);

            // A plain member of a guild whose key was removed still reads the statuses.
            await db.GuildMembers.Where(member => member.GuildId == guild.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(member => member.Role, GuildRole.Member), Ct);
            await db.Guilds.Where(entity => entity.Id == guild.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(entity => entity.GuildApiToken, (string?)null), Ct);
        }

        var calls = (factory.TacticusApi.GuildCallCount(guild.Token), factory.TacticusApi.GuildRaidCallCount(guild.Token),
            factory.TacticusApi.GuildRaidSeasonCalls(guild.Token).Count);
        var body = await guild.Client.GetStringAsync(SeasonsPath, Ct);
        Assert.Equal(calls, (factory.TacticusApi.GuildCallCount(guild.Token), factory.TacticusApi.GuildRaidCallCount(guild.Token),
            factory.TacticusApi.GuildRaidSeasonCalls(guild.Token).Count));

        using var json = JsonDocument.Parse(body);
        Assert.Equal(["currentSeasonNumber", "seasons"], json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(Active, json.RootElement.GetProperty("currentSeasonNumber").GetInt32());
        var seasons = json.RootElement.GetProperty("seasons").EnumerateArray().ToList();
        Assert.Equal(
            [(114, "live", true), (113, "final", true), (112, "pending", false), (111, "final", false), (103, "unavailable", true)],
            seasons.Select(season => (season.GetProperty("seasonNumber").GetInt32(),
                season.GetProperty("status").GetString()!, season.GetProperty("hasHits").GetBoolean())));
        Assert.Equal(
            ["seasonNumber", "seasonConfigId", "status", "hasHits", "skippedEntryCount", "observedAt", "finalizedAt"],
            seasons[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal(1, seasons[1].GetProperty("skippedEntryCount").GetInt32());
        Assert.Equal(JsonValueKind.String, seasons[1].GetProperty("finalizedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, seasons[0].GetProperty("finalizedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, seasons[2].GetProperty("seasonConfigId").ValueKind);
        Assert.Equal(JsonValueKind.Null, seasons[2].GetProperty("observedAt").ValueKind);
    }

    [Fact]
    public async Task SeasonsOfAGuildWithoutAnyIsEmptyAndWithoutAGuildConflicts()
    {
        var unprovisioned = factory.CreateClient();
        unprovisioned.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, $"seasons-{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, (await unprovisioned.GetAsync(SeasonsPath, Ct)).StatusCode);

        var (unlinked, _) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var conflict = await unlinked.GetAsync(SeasonsPath, Ct);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("guildRaidNotReady",
            (await conflict.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("issueCode").GetString());

        var guild = await RegisterAsync();
        var empty = await guild.Client.GetFromJsonAsync<GuildRaidSeasonsResponse>(SeasonsPath, Ct);
        Assert.Null(empty!.CurrentSeasonNumber);
        Assert.Empty(empty.Seasons);
    }

    private sealed record TestGuild(
        HttpClient Client, string Token, string Tag, GuildId Id, Guid TacticusGuildId, Guid TacticusUserId)
    {
        public GuildRaidEntry LiveEntry { get; set; } = null!;

        /// <summary>The oldest season its runs ask for.</summary>
        public int FirstSeason { get; set; } = Active;
    }

    private async Task<TestGuild> RegisterAsync()
    {
        var (client, tacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var token = $"capture-token-{Guid.NewGuid()}";
        var guild = new TestGuild(client, token, $"S{Guid.NewGuid():N}"[..10], GuildId.From(Guid.NewGuid()), Guid.NewGuid(), tacticusUserId);
        factory.TacticusApi.ConfigureGuildResponse(token, Upstream(guild.TacticusGuildId, guild));
        (await client.PostAsJsonAsync("/api/v1/guild/register", new RegisterGuildRequest(token), Ct)).EnsureSuccessStatusCode();
        await using var db = Db();
        return guild with { Id = await db.Guilds.Where(entity => entity.Tag == guild.Tag).Select(entity => entity.Id).SingleAsync(Ct) };
    }

    private async Task<TestGuild> ActiveGuildAsync(int firstSeason)
    {
        var guild = await RegisterAsync();
        guild.FirstSeason = firstSeason;
        await ActivateAsync(guild, Active);
        return guild;
    }

    private async Task ActivateAsync(TestGuild guild, int number)
    {
        var config = factory.Services.GetRequiredService<IGameCatalogProvider>().Current.RaidBossesView.Seasons
            .OrderBy(pair => pair.Key, StringComparer.Ordinal).First();
        var tier = config.Value.Tiers[0];
        var boss = tier.Sets[0].Encounters.First(encounter => encounter.EncounterType == "Boss");
        var entry = Entry();
        entry.Tier = tier.Tier;
        entry.Rarity = (Rarity)tier.Tier;
        entry.Set = tier.Sets[0].Set;
        entry.EncounterIndex = boss.EncounterIndex;
        entry.UnitId = boss.UnitSetId;
        guild.LiveEntry = entry;
        factory.TacticusApi.ConfigureGuildRaidResponse(guild.Token, new GuildRaidResponse
        {
            Season = number,
            SeasonConfigId = config.Key,
            Entries = [entry],
        });
        if (number != Active)
        {
            await ExpireRefreshCooldownAsync(guild);
        }

        (await guild.Client.PostAsync(RefreshPath, null, Ct)).EnsureSuccessStatusCode();
    }

    private static TacticusApi.Models.Guild.GuildResponse Upstream(
        Guid guildId, TestGuild guild, UpstreamGuildRole role = UpstreamGuildRole.LEADER, List<int>? listed = null)
    {
        var response = FakeTacticusApi.BuildGuildResponse(
            guildId, guild.Tag, "Capture Guild", 10, (guild.TacticusUserId, role, 50, null));
        response.Guild.GuildRaidSeasons = listed ?? [];
        return response;
    }

    private static GuildRaidResponse Season(int number, params GuildRaidEntry[] entries) => new()
    {
        Season = number,
        SeasonConfigId = "guild_boss_season_config_1",
        Entries = [.. entries],
    };

    private static GuildRaidEntry Entry() => new()
    {
        UserId = Guid.NewGuid(),
        Tier = 4,
        Set = 1,
        EncounterType = EncounterType.Boss,
        UnitId = "guildBossRetired:2",
        Rarity = Rarity.Legendary,
        RemainingHp = 500,
        MaxHp = 1000,
        DamageDealt = 500,
        DamageType = DamageType.Battle,
        StartedOn = 1_780_000_000,
        CompletedOn = 1_780_000_100,
        HeroDetails = [new GuildRaidUnit { UnitId = "ultraTigurius", Power = 5700 }],
        GlobalConfigHash = "config",
    };

    private static GuildRaidEntry Copy(GuildRaidEntry entry) =>
        JsonSerializer.Deserialize<GuildRaidEntry>(JsonSerializer.Serialize(entry))!;

    private async Task StoreSeasonAsync(TestGuild guild, int number, params GuildRaidEntry[] entries)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var hash = scope.ServiceProvider.GetRequiredService<Persistence.Encryption.IColumnHashService>();
        var season = new GuildRaidSeason
        {
            Id = GuildRaidSeasonId.From(Guid.CreateVersion7()),
            GuildId = guild.Id,
            SeasonNumber = number,
            SeasonConfigId = "guild_boss_season_config_1",
            ObservedAt = DateTimeOffset.UtcNow,
        };
        foreach (var entry in entries)
        {
            Assert.True(GuildRaidEntryNormalizer.TryBuildPastAttack(entry, hash, out var attack));
            season.Attacks.Add(attack);
        }

        db.GuildRaidSeasons.Add(season);
        await db.SaveChangesAsync(Ct);
    }

    private async Task RunAsync(TestGuild guild, CancellationToken? ct = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<GuildRaidSeasonCaptureService>();
        service.RequestPause = TimeSpan.Zero;
        service.FirstSeason = guild.FirstSeason;
        await service.RunAsync(guild.Id, ct ?? Ct);
    }

    private async Task RunAgainAsync(TestGuild guild)
    {
        await SetNotBeforeAsync(guild, null);
        await RunAsync(guild);
    }

    /// <summary>Runs as if seasons had to be planned again, as when a new season starts.</summary>
    private async Task ReplanAsync(TestGuild guild)
    {
        await using (var db = Db())
        {
            await db.GuildRaidSyncStates.Where(state => state.GuildId == guild.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(state => state.SeasonsPlannedFor, (int?)null), Ct);
        }

        await RunAgainAsync(guild);
    }

    private async Task SetNotBeforeAsync(TestGuild guild, DateTimeOffset? value)
    {
        await using var db = Db();
        await db.GuildRaidSyncStates.Where(state => state.GuildId == guild.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(state => state.CaptureNotBefore, value), Ct);
    }

    private async Task ExpireRefreshCooldownAsync(TestGuild guild)
    {
        await using var db = Db();
        var expired = DateTimeOffset.UtcNow - GuildRaidStatusService.CooldownWindow - TimeSpan.FromSeconds(1);
        await db.GuildRaidSyncStates.Where(state => state.GuildId == guild.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(state => state.LastAttemptedAt, expired), Ct);
    }

    private PlannerDbContext Db() => factory.Services.CreateScope().ServiceProvider.GetRequiredService<PlannerDbContext>();

    private static Task<GuildRaidSyncState> StateAsync(PlannerDbContext db, TestGuild guild) =>
        db.GuildRaidSyncStates.AsNoTracking().SingleAsync(state => state.GuildId == guild.Id, Ct);

    private static Task<List<GuildRaidSeasonCapture>> CapturesAsync(PlannerDbContext db, TestGuild guild) =>
        db.GuildRaidSeasonCaptures.AsNoTracking().Where(capture => capture.GuildId == guild.Id).ToListAsync(Ct);

    private async Task<GuildRaidSeasonCaptureStatus> StatusOfAsync(TestGuild guild, int season)
    {
        await using var db = Db();
        return (await CapturesAsync(db, guild)).Single(capture => capture.SeasonNumber == season).Status;
    }

    private static Task<int> HitsAsync(PlannerDbContext db, TestGuild guild, int season) =>
        db.GuildRaidAttacks.CountAsync(
            attack => attack.Season!.GuildId == guild.Id && attack.Season.SeasonNumber == season, Ct);

    private static void AssertRetryInAnHour(GuildRaidSyncState state) =>
        Assert.InRange(
            state.CaptureNotBefore!.Value,
            DateTimeOffset.UtcNow.AddMinutes(58),
            DateTimeOffset.UtcNow.AddMinutes(61));
}
