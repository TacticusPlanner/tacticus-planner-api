using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.Domain.GuildRaids;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.Persistence;
using TacticusPlanner.TacticusApi;
using TacticusPlanner.TacticusApi.Models.GuildRaid;
using TacticusPlanner.TacticusApi.Models.Shared;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>The read of the season that ended, done by the first raid refresh of the next season.</summary>
public sealed class GuildRaidEndedSeasonTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string StatusPath = "/api/v1/guild/raid-status";
    private const string RefreshPath = "/api/v1/guild/raid-status/refresh";
    private const int Ended = 113;
    private const int Active = 114;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FirstRefreshOfANewSeasonReadsTheEndedOneOnceAndLeavesLiveStatusAlone()
    {
        var guild = await RegisterAsync();
        await ActivateAsync(guild, Ended);
        var lastLive = Copy(guild.LiveEntry);
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(Ended, lastLive, Entry()));

        var refreshed = await ActivateAsync(guild, Active);

        Assert.Equal([Ended], factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));
        Assert.Equal(refreshed, await guild.Client.GetStringAsync(StatusPath, Ct));
        await using (var db = Db())
        {
            Assert.Equal(2, await HitsAsync(db, guild, Ended));
            Assert.NotNull(await FinalizedAtAsync(db, guild, Ended));
            Assert.Null(await FinalizedAtAsync(db, guild, Active));
            AssertCleared(await StateAsync(db, guild));
        }

        await RefreshAsync(guild);
        Assert.Single(factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));
    }

    [Fact]
    public async Task SameSeasonAndNoActiveSeasonReadNothingUntilANewSeasonStarts()
    {
        var guild = await RegisterAsync();
        await ActivateAsync(guild, Ended);
        await ActivateAsync(guild, Ended);
        factory.TacticusApi.ConfigureGuildRaidResponse(guild.Token, new GuildRaidResponse());
        await RefreshAsync(guild);

        Assert.Empty(factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));
        await using (var db = Db())
        {
            Assert.Null((await StateAsync(db, guild)).EndedSeasonNumber);
        }

        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(Ended, Entry()));
        await ActivateAsync(guild, Active);
        Assert.Equal([Ended], factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));
    }

    [Fact]
    public async Task OnlyTheLastSeasonTheGuildSawIsReadAfterSeveralWereMissed()
    {
        var guild = await RegisterAsync();
        await ActivateAsync(guild, 110);
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(110, Entry()));

        await ActivateAsync(guild, Active);

        Assert.Equal([110], factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));
    }

    [Fact]
    public async Task EndedHitsAreStoredFromTheirOwnDataWhateverTodaysCatalogSays()
    {
        var guild = await RegisterAsync();
        await StoreSeasonAsync(guild, Ended);
        var suffixed = Entry();
        suffixed.UnitId = "guildBossRetired:3";
        suffixed.Tier = 7;
        var plain = Entry();
        plain.UnitId = "guildBossRetired";
        var response = Season(Ended, suffixed, plain);
        response.SeasonConfigId = "config-the-catalog-never-had";
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, response);

        await ActivateAsync(guild, Active);

        await using var db = Db();
        var hits = await db.GuildRaidAttacks.Where(attack => attack.Season!.GuildId == guild.Id
            && attack.Season.SeasonNumber == Ended).ToListAsync(Ct);
        Assert.All(hits, hit => Assert.Equal("guildBossRetired", hit.UnitSetId));
        var withSuffix = Assert.Single(hits, hit => hit.ProgressionIndex == 3);
        Assert.Equal(7, withSuffix.Tier);
        Assert.Equal(suffixed.UserId.ToString()[^4..], withSuffix.TacticusUserIdSuffix);
        Assert.Equal("ultraTigurius", Assert.Single(withSuffix.Units).UnitId);
        Assert.Single(hits, hit => hit.ProgressionIndex is null);
        Assert.NotNull(await FinalizedAtAsync(db, guild, Ended));
    }

    [Fact]
    public async Task MalformedEntriesAreSkipped()
    {
        var guild = await RegisterAsync();
        await StoreSeasonAsync(guild, Ended);
        var unfinished = Entry();
        unfinished.CompletedOn = null;
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(Ended, Entry(), unfinished, Entry()));

        await ActivateAsync(guild, Active);

        await using var db = Db();
        Assert.Equal(2, await HitsAsync(db, guild, Ended));
        Assert.NotNull(await FinalizedAtAsync(db, guild, Ended));
    }

    [Fact]
    public async Task ReadAddsToLiveHitsWithoutDuplicatingOrDeletingAnyAndFillsTheirSuffix()
    {
        var guild = await RegisterAsync();
        var same = Entry();
        var rehashed = Entry();
        var onlyStored = Entry();
        await StoreSeasonAsync(guild, Ended, same, rehashed, onlyStored);
        await using (var db = Db())
        {
            await db.GuildRaidAttacks.Where(attack => attack.Season!.GuildId == guild.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(attack => attack.TacticusUserIdSuffix, (string?)null), Ct);
        }

        var again = Copy(rehashed);
        again.GlobalConfigHash = "a later config";
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(Ended, Copy(same), again, Entry()));
        await ActivateAsync(guild, Active);

        await using var verify = Db();
        var hits = await verify.GuildRaidAttacks.Where(attack => attack.Season!.GuildId == guild.Id
            && attack.Season.SeasonNumber == Ended).ToListAsync(Ct);
        Assert.Equal(4, hits.Count);
        Assert.All([same, rehashed], entry =>
            Assert.Single(hits, hit => hit.TacticusUserIdSuffix == entry.UserId.ToString()[^4..]));
        // The hit the response left out is kept, and nothing says whose it is.
        Assert.Single(hits, hit => hit.TacticusUserIdSuffix is null);
        Assert.NotNull(await FinalizedAtAsync(verify, guild, Ended));
    }

    [Fact]
    public async Task EmptyResponseIsBelievedOnlyWhenNothingWasSeenOfTheSeason()
    {
        var seen = await RegisterAsync();
        await StoreSeasonAsync(seen, Ended, Entry());
        factory.TacticusApi.ConfigureGuildRaidSeason(seen.Token, Season(Ended));
        var unseen = await RegisterAsync();
        await StoreSeasonAsync(unseen, Ended);
        factory.TacticusApi.ConfigureGuildRaidSeason(unseen.Token, Season(Ended));

        await ActivateAsync(seen, Active);
        await ActivateAsync(unseen, Active);

        await using var db = Db();
        Assert.Equal(1, await HitsAsync(db, seen, Ended));
        Assert.Null(await FinalizedAtAsync(db, seen, Ended));
        Assert.Equal(Ended, (await StateAsync(db, seen)).EndedSeasonNumber);
        Assert.NotNull(await FinalizedAtAsync(db, unseen, Ended));
        AssertCleared(await StateAsync(db, unseen));
    }

    [Fact]
    public async Task WrongSeasonNumberStoresNothing()
    {
        var guild = await RegisterAsync();
        await StoreSeasonAsync(guild, Ended);
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Ended, () => Task.FromResult(Season(112, Entry())));

        await ActivateAsync(guild, Active);

        await using var db = Db();
        Assert.Equal(0, await HitsAsync(db, guild, Ended) + await HitsAsync(db, guild, 112));
        Assert.Equal(Ended, (await StateAsync(db, guild)).EndedSeasonNumber);
    }

    [Theory]
    [InlineData("400")]
    [InlineData("403")]
    [InlineData("500")]
    [InlineData("timeout")]
    [InlineData("json")]
    [InlineData("empty")]
    [InlineData("rarity")]
    [InlineData("timestamp")]
    [InlineData("units")]
    public async Task FailureKeepsTheRefreshAndIsRetriedAfterAnHour(string failure)
    {
        var guild = await RegisterAsync();
        await StoreSeasonAsync(guild, Ended, Entry());
        var bad = Entry();
        switch (failure)
        {
            case "400" or "403" or "500":
                factory.TacticusApi.ConfigureGuildRaidSeasonRejection(guild.Token, Ended, (HttpStatusCode)int.Parse(failure, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case "timeout":
                factory.TacticusApi.ConfigureGuildRaidSeason(
                    guild.Token, Ended, () => throw new TaskCanceledException("Simulated timeout."));
                break;
            case "json":
                factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Ended, RealClientReadingUnreadableBodyAsync);
                break;
            case "empty":
                factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Ended, () => Task.FromResult<GuildRaidResponse>(null!));
                break;
            case "rarity": bad.Rarity = (Rarity)99; break;
            case "timestamp": bad.CompletedOn = long.MaxValue; break;
            case "units": bad.HeroDetails = null!; break;
        }

        if (failure is "rarity" or "timestamp" or "units")
        {
            factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(Ended, Entry(), bad));
        }

        var refreshed = await ActivateAsync(guild, Active);
        Assert.Contains("\"state\":\"active\"", refreshed);
        await RefreshAsync(guild);
        Assert.Equal([Ended], factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));

        await using (var db = Db())
        {
            Assert.Equal(1, await HitsAsync(db, guild, Ended));
            Assert.Null(await FinalizedAtAsync(db, guild, Ended));
            var state = await StateAsync(db, guild);
            Assert.Equal(Ended, state.EndedSeasonNumber);
            Assert.InRange(state.EndedSeasonAttemptedAt!.Value, DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow);
        }

        await MoveAttemptBackAsync(guild);
        await RefreshAsync(guild);
        Assert.Equal([Ended, Ended], factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));
    }

    // The real client, so the test holds whatever exception Refit raises for a successful but unreadable body.
    private static async Task<GuildRaidResponse> RealClientReadingUnreadableBodyAsync()
    {
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(http => http.AddHttpMessageHandler(() => new UnreadableBodyHandler()));
        services.AddTacticusApi("https://tacticus.test");
        await using var provider = services.BuildServiceProvider();
        return await provider.GetRequiredService<ITacticusApi>().GetGuildRaidBySeasonAsync("token", Ended, Ct);
    }

    private sealed class UnreadableBodyHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent("{\"season\":113,\"entries\":[{\"tier\":\"x\"}]}", Encoding.UTF8, "application/json"),
            });
    }

    [Fact]
    public async Task ThirdNotFoundGivesTheSeasonUpAndKeepsItsHits()
    {
        var guild = await RegisterAsync();
        await StoreSeasonAsync(guild, Ended, Entry());
        factory.TacticusApi.ConfigureGuildRaidSeasonRejection(guild.Token, Ended, HttpStatusCode.NotFound);

        await ActivateAsync(guild, Active);
        foreach (var _ in Enumerable.Range(0, 2))
        {
            await MoveAttemptBackAsync(guild);
            await RefreshAsync(guild);
        }

        Assert.Equal([Ended, Ended, Ended], factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));
        await using (var db = Db())
        {
            AssertCleared(await StateAsync(db, guild));
            Assert.Null(await FinalizedAtAsync(db, guild, Ended));
            Assert.Equal(1, await HitsAsync(db, guild, Ended));
        }

        await MoveAttemptBackAsync(guild);
        await RefreshAsync(guild);
        Assert.Equal(3, factory.TacticusApi.GuildRaidSeasonCalls(guild.Token).Count);
    }

    [Fact]
    public async Task ConcurrentReadsClaimTheSeasonOnce()
    {
        var guild = await RegisterAsync();
        await StoreSeasonAsync(guild, Ended);
        factory.TacticusApi.ConfigureGuildRaidSeasonRejection(guild.Token, Ended, HttpStatusCode.ServiceUnavailable);
        await ActivateAsync(guild, Active);
        await MoveAttemptBackAsync(guild);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Ended, async () =>
        {
            await gate.Task;
            return Season(Ended, Entry());
        });
        try
        {
            // Two instances: each has its own scope and none shares the coordinator's flight.
            var first = ReadDirectlyAsync(guild);
            var second = ReadDirectlyAsync(guild);
            await Task.WhenAny(Task.WhenAll(first, second), WaitForCallsAsync(guild, 2));
            gate.TrySetResult();
            await Task.WhenAll(first, second);
        }
        finally
        {
            gate.TrySetResult();
        }

        Assert.Equal(2, factory.TacticusApi.GuildRaidSeasonCalls(guild.Token).Count);
        await using var db = Db();
        Assert.Equal(1, await HitsAsync(db, guild, Ended));
    }

    [Fact]
    public async Task JoinedRefreshesWaitForOneRead()
    {
        var guild = await RegisterAsync();
        await StoreSeasonAsync(guild, Ended);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Ended, async () =>
        {
            await gate.Task;
            return Season(Ended, Entry());
        });
        ConfigureLive(guild, Active);
        await ExpireRefreshCooldownAsync(guild);

        try
        {
            var first = guild.Client.PostAsync(RefreshPath, null, Ct);
            await WaitForCallsAsync(guild, 1);
            var second = guild.Client.PostAsync(RefreshPath, null, Ct);
            Assert.False(second.IsCompleted);
            gate.TrySetResult();
            (await first).EnsureSuccessStatusCode();
            (await second).EnsureSuccessStatusCode();
        }
        finally
        {
            gate.TrySetResult();
        }

        Assert.Single(factory.TacticusApi.GuildRaidSeasonCalls(guild.Token));
        await using var db = Db();
        Assert.Equal(1, await HitsAsync(db, guild, Ended));
    }

    [Fact]
    public async Task FullTacticusUserIdIsInNoRaidColumn()
    {
        var guild = await RegisterAsync();
        await StoreSeasonAsync(guild, Ended);
        var entry = Entry();
        factory.TacticusApi.ConfigureGuildRaidSeason(guild.Token, Season(Ended, entry));
        await ActivateAsync(guild, Active);

        await using var db = Db();
        foreach (var table in new[] { "guild_raid_attacks", "guild_raid_seasons", "guild_raid_sync_states" })
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
        var guild = await RegisterAsync();
        await ActivateAsync(guild, Active);
        await using (var db = Db())
        {
            await db.GuildRaidAttacks.Where(attack => attack.Season!.GuildId == guild.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(attack => attack.TacticusUserIdSuffix, (string?)null), Ct);
        }

        await RefreshAsync(guild);

        await using var verify = Db();
        Assert.Equal(
            guild.LiveEntry.UserId.ToString()[^4..],
            await verify.GuildRaidAttacks.Where(attack => attack.Season!.GuildId == guild.Id)
                .Select(attack => attack.TacticusUserIdSuffix).SingleAsync(Ct));
    }

    private sealed record TestGuild(HttpClient Client, string Token, string Tag, GuildId Id, Guid TacticusUserId)
    {
        public GuildRaidEntry LiveEntry { get; set; } = null!;
    }

    private async Task<TestGuild> RegisterAsync()
    {
        var (client, tacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var token = $"ended-token-{Guid.NewGuid()}";
        var guild = new TestGuild(client, token, $"E{Guid.NewGuid():N}"[..10], GuildId.From(Guid.NewGuid()), tacticusUserId);
        factory.TacticusApi.ConfigureGuildResponse(token, FakeTacticusApi.BuildGuildResponse(
            Guid.NewGuid(), guild.Tag, "Ended Season Guild", 10,
            (tacticusUserId, TacticusApi.Models.Guild.GuildRole.LEADER, 50, null)));
        (await client.PostAsJsonAsync("/api/v1/guild/register", new RegisterGuildRequest(token), Ct)).EnsureSuccessStatusCode();
        await using var db = Db();
        return guild with { Id = await db.Guilds.Where(entity => entity.Tag == guild.Tag).Select(entity => entity.Id).SingleAsync(Ct) };
    }

    /// <summary>Makes <paramref name="number"/> the live season, one hit in, and refreshes.</summary>
    /// <returns>The refresh response body.</returns>
    private async Task<string> ActivateAsync(TestGuild guild, int number)
    {
        ConfigureLive(guild, number);
        await ExpireRefreshCooldownAsync(guild);
        var response = await guild.Client.PostAsync(RefreshPath, null, Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private void ConfigureLive(TestGuild guild, int number)
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
    }

    private async Task RefreshAsync(TestGuild guild)
    {
        await ExpireRefreshCooldownAsync(guild);
        (await guild.Client.PostAsync(RefreshPath, null, Ct)).EnsureSuccessStatusCode();
    }

    private async Task ReadDirectlyAsync(TestGuild guild)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<GuildRaidEndedSeasonService>().ReadIfDueAsync(guild.Id, guild.Token, Ct);
    }

    private async Task WaitForCallsAsync(TestGuild guild, int count)
    {
        while (factory.TacticusApi.GuildRaidSeasonCalls(guild.Token).Count < count)
        {
            await Task.Delay(10, Ct);
        }
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

    /// <summary>A season the guild stored before, as if seen live.</summary>
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

    private async Task ExpireRefreshCooldownAsync(TestGuild guild)
    {
        await using var db = Db();
        var expired = DateTimeOffset.UtcNow - GuildRaidStatusService.CooldownWindow - TimeSpan.FromSeconds(1);
        await db.GuildRaidSyncStates.Where(state => state.GuildId == guild.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(state => state.LastAttemptedAt, expired), Ct);
    }

    private async Task MoveAttemptBackAsync(TestGuild guild)
    {
        await using var db = Db();
        var past = DateTimeOffset.UtcNow - GuildRaidEndedSeasonService.RetryDelay - TimeSpan.FromMinutes(1);
        await db.GuildRaidSyncStates.Where(state => state.GuildId == guild.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(state => state.EndedSeasonAttemptedAt, past), Ct);
    }

    private PlannerDbContext Db() => factory.Services.CreateScope().ServiceProvider.GetRequiredService<PlannerDbContext>();

    private static Task<GuildRaidSyncState> StateAsync(PlannerDbContext db, TestGuild guild) =>
        db.GuildRaidSyncStates.AsNoTracking().SingleAsync(state => state.GuildId == guild.Id, Ct);

    private static Task<DateTimeOffset?> FinalizedAtAsync(PlannerDbContext db, TestGuild guild, int season) =>
        db.GuildRaidSeasons.Where(entity => entity.GuildId == guild.Id && entity.SeasonNumber == season)
            .Select(entity => entity.FinalizedAt).SingleAsync(Ct);

    private static Task<int> HitsAsync(PlannerDbContext db, TestGuild guild, int season) =>
        db.GuildRaidAttacks.CountAsync(
            attack => attack.Season!.GuildId == guild.Id && attack.Season.SeasonNumber == season, Ct);

    private static void AssertCleared(GuildRaidSyncState state)
    {
        Assert.Null(state.EndedSeasonNumber);
        Assert.Null(state.EndedSeasonAttemptedAt);
        Assert.Equal(0, state.EndedSeasonNotFoundCount);
    }
}
