using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.LegendaryEventPlans;
using TacticusPlanner.Api.Features.RosterSnapshots;
using TacticusPlanner.Api.Features.TacticusIntegration;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.RosterSnapshots;
using TacticusPlanner.GameDomain;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary><c>personal-roster-snapshots</c>: create from a fresh sync or the last sync, the limit, names, list,
/// read, rename, delete, ownership, rate limiting and purge.</summary>
public sealed class PersonalRosterSnapshotEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string Url = "/api/v1/me/roster-snapshots";
    private const string FromLastSync = Url + "?fromLastSync=true";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASnapshotCopiesTheFreshlySyncedRoster()
    {
        var (client, token, profileId) = await CreateSyncingClientAsync();
        factory.TacticusApi.ConfigurePlayerResponse(token, FakeTacticusApi.BuildPlayerResponse(rank: 13));

        var created = await CreateAsync(client, Url, "Before LE");

        Assert.Equal(("Before LE", RosterSnapshotSource.Manual, 1, 0),
            (created.Name, created.Source, created.CharacterCount, created.MowCount));
        var roster = await ReadRosterAsync(client, created.Id);
        Assert.Equal(created.TakenAt, roster.TakenAt);
        var unit = Assert.Single(roster.Characters);
        Assert.Equal((FakeTacticusApi.CharacterUnitId, (UnitRank)13), (unit.UnitId.Value, unit.Rank));
        // The sync itself was written too.
        Assert.Equal(created.TakenAt, await SyncedAtAsync(profileId));
    }

    [Fact]
    public async Task ARepeatSyncWithAnUnchangedRosterStillCapturesEveryUnit()
    {
        var (client, _, _) = await CreateSyncingClientAsync();
        (await client.PostAsync("/api/v1/player-data/sync", null, Ct)).EnsureSuccessStatusCode();

        var created = await CreateAsync(client, Url, null);

        var unit = Assert.Single((await ReadRosterAsync(client, created.Id)).Characters);
        Assert.Equal(FakeTacticusApi.CharacterUnitId, unit.UnitId.Value);
        Assert.Equal(35, Assert.Single(unit.Abilities).Level);
    }

    [Fact]
    public async Task ASyncFailureCreatesNothing()
    {
        var (client, token, _) = await CreateSyncingClientAsync();
        factory.TacticusApi.ConfigurePlayerTimeout(token);

        var response = await PostAsync(client, Url, "Down");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("upstreamUnavailable", (await response.ReadProblemAsync())["issueCode"]!.GetValue<string>());
        Assert.Empty((await ListAsync(client)).Items);
    }

    [Fact]
    public async Task TheFallbackCopiesTheLastSyncWithoutCallingUpstream()
    {
        var (client, token, profileId) = await CreateSyncingClientAsync();
        (await client.PostAsync("/api/v1/player-data/sync", null, Ct)).EnsureSuccessStatusCode();
        var syncedAt = await SyncedAtAsync(profileId);
        factory.TacticusApi.ConfigurePlayerTimeout(token);

        var created = await CreateAsync(client, FromLastSync, "Stale");

        Assert.Equal(syncedAt, created.TakenAt);
        Assert.Equal(FakeTacticusApi.CharacterUnitId, Assert.Single((await ReadRosterAsync(client, created.Id)).Characters).UnitId.Value);
    }

    [Fact]
    public async Task TheFallbackIsRejectedWhenThePlayerHasNeverSynced()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await PostAsync(client, FromLastSync, null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("playerDataNotSynced", (await response.ReadProblemAsync())["issueCode"]!.GetValue<string>());
    }

    [Fact]
    public async Task ACreateWithoutATacticusKeyIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, Url, null)).StatusCode);
    }

    [Fact]
    public async Task AtTheLimitACreateIsRejectedBeforeAnySync()
    {
        var (client, token, profileId) = await CreateSyncingClientAsync();
        await SeedAsync(profileId, Enumerable.Range(0, 20).Select(day => (RosterSnapshotSource.Manual, Day(day))));
        // Upstream would fail; the limit answers first, so no sync is attempted.
        factory.TacticusApi.ConfigurePlayerTimeout(token);
        var attemptedBefore = await LastAttemptedAtAsync(profileId);

        var response = await PostAsync(client, Url, "One more");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadProblemAsync();
        Assert.Equal("rosterSnapshotLimitReached", problem["issueCode"]!.GetValue<string>());
        Assert.Equal((20, 20), (problem["limit"]!.GetValue<int>(), problem["count"]!.GetValue<int>()));
        Assert.Equal(20, (await ListAsync(client)).Items.Count);
        Assert.Equal(attemptedBefore, await LastAttemptedAtAsync(profileId));
    }

    [Fact]
    public async Task AutomaticSnapshotsAndRunRecordsDoNotCountTowardTheLimit()
    {
        var (client, _, profileId) = await CreateSyncingClientAsync();
        await SeedAsync(profileId, Enumerable.Range(0, 19).Select(day => (RosterSnapshotSource.Manual, Day(day)))
            .Concat(Enumerable.Range(19, 3).Select(day => (RosterSnapshotSource.Auto, Day(day)))));
        await SeedRunRecordAsync(profileId);

        await CreateAsync(client, Url, null);

        var list = await ListAsync(client);
        Assert.Equal((20, 20, 23), (list.Limit, list.Count, list.Items.Count));
    }

    [Fact]
    public async Task ConcurrentCreatesDoNotExceedTheLimit()
    {
        var (client, _, profileId) = await CreateSyncingClientAsync();
        (await client.PostAsync("/api/v1/player-data/sync", null, Ct)).EnsureSuccessStatusCode();
        await SeedAsync(profileId, Enumerable.Range(0, 19).Select(day => (RosterSnapshotSource.Manual, Day(day))));

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PostAsync(client, FromLastSync, null)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.All(responses.Where(response => response.StatusCode != HttpStatusCode.OK),
            response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode));
        Assert.Equal(20, (await ListAsync(client)).Count);
    }

    [Fact]
    public async Task DeletingASnapshotFreesASlot()
    {
        var (client, _, profileId) = await CreateSyncingClientAsync();
        await SeedAsync(profileId, Enumerable.Range(0, 20).Select(day => (RosterSnapshotSource.Manual, Day(day))));
        var oldest = (await ListAsync(client)).Items[^1];

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Url}/{oldest.Id}", Ct)).StatusCode);
        await CreateAsync(client, Url, null);

        var list = await ListAsync(client);
        Assert.Equal(20, list.Count);
        Assert.DoesNotContain(list.Items, item => item.Id == oldest.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Url}/{oldest.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task ABlankNameBecomesTheCaptureDateAndNamesNeedNotBeUnique()
    {
        var (client, _, _) = await CreateSyncingClientAsync();

        var unnamed = await CreateAsync(client, Url, "   ");
        var first = await CreateAsync(client, Url, "  Weekly ");
        var second = await CreateAsync(client, Url, "Weekly");

        Assert.Equal(unnamed.TakenAt.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), unnamed.Name);
        Assert.Equal(("Weekly", "Weekly"), (first.Name, second.Name));
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task ANameLongerThanSixtyCharactersIsRejected()
    {
        var (client, _, _) = await CreateSyncingClientAsync();

        var response = await PostAsync(client, Url, new string('x', 61));

        await response.AssertFieldRejectedAsync("name");
        Assert.Empty((await ListAsync(client)).Items);
    }

    [Fact]
    public async Task RenameTrimsAndKeepsTheRoster()
    {
        var (client, _, _) = await CreateSyncingClientAsync();
        var created = await CreateAsync(client, Url, "Before");
        var rosterBefore = await ReadRosterAsync(client, created.Id);

        var response = await client.PatchAsJsonAsync(
            $"{Url}/{created.Id}", new RenamePersonalRosterSnapshotRequest("  After Ragnar  "), Ct);

        response.EnsureSuccessStatusCode();
        var renamed = (await response.Content.ReadFromJsonAsync<PersonalRosterSnapshotResponse>(Ct))!;
        Assert.Equal(created with { Name = "After Ragnar" }, renamed);
        Assert.Equivalent(rosterBefore, await ReadRosterAsync(client, created.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task RenameRejectsABlankName(string? name)
    {
        var (client, _, _) = await CreateSyncingClientAsync();
        var created = await CreateAsync(client, Url, "Keep");

        var response = await client.PatchAsJsonAsync($"{Url}/{created.Id}", new RenamePersonalRosterSnapshotRequest(name), Ct);

        await response.AssertFieldRejectedAsync("name");
        Assert.Equal("Keep", Assert.Single((await ListAsync(client)).Items).Name);
    }

    [Fact]
    public async Task TheListIsNewestFirstWithoutRosters()
    {
        var (client, _, profileId) = await CreateSyncingClientAsync();
        await SeedAsync(profileId, [(RosterSnapshotSource.Manual, Day(0)), (RosterSnapshotSource.Manual, Day(31))]);

        var list = await ListAsync(client);

        Assert.Equal((20, 2), (list.Limit, list.Count));
        Assert.Equal([Day(31), Day(0)], list.Items.Select(item => item.TakenAt));
    }

    [Fact]
    public async Task AnotherProfilesSnapshotIsNotFound()
    {
        var (owner, _, _) = await CreateSyncingClientAsync();
        var created = await CreateAsync(owner, Url, "Mine");
        var other = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var url = $"{Url}/{created.Id}";

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.PatchAsJsonAsync(url, new RenamePersonalRosterSnapshotRequest("Theirs"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync(url, Ct)).StatusCode);
        Assert.Empty((await ListAsync(other)).Items);
        Assert.Equal("Mine", Assert.Single((await ListAsync(owner)).Items).Name);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"{Url}/not-a-uuid", Ct)).StatusCode);
    }

    [Fact]
    public async Task TheSyncingCreateIsRateLimitedButTheFallbackIsNot()
    {
        using var limited = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:UpstreamSlowPerHour", "1"));
        var client = limited.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, $"snap-limit-{Guid.NewGuid()}");
        (await client.GetAsync("/api/v1/account", Ct)).EnsureSuccessStatusCode();
        var token = $"snap-limit-{Guid.NewGuid()}";
        factory.TacticusApi.ConfigurePlayerResponse(token, FakeTacticusApi.BuildPlayerResponse());
        (await client.PutAsJsonAsync("/api/v1/account/tacticus-integration", new UpdateTacticusIntegrationRequest(token, null), Ct))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, Url, null)).StatusCode);
        var limitedResponse = await PostAsync(client, Url, null);

        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
        Assert.NotNull(limitedResponse.Headers.RetryAfter);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, FromLastSync, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, FromLastSync, null)).StatusCode);
    }

    [Fact]
    public async Task PurgeRemovesEverySnapshot()
    {
        var (client, _, profileId) = await CreateSyncingClientAsync();
        await CreateAsync(client, Url, null);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/v1/account", Ct)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        Assert.False(await db.PersonalRosterSnapshots.IgnoreQueryFilters().AnyAsync(entity => entity.ProfileId == profileId, Ct));
    }

    private static DateTimeOffset Day(int offset) => new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero).AddDays(offset);

    private async Task<(HttpClient Client, string Token, ProfileId ProfileId)> CreateSyncingClientAsync()
    {
        var subject = $"snap-{Guid.NewGuid()}";
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory, subject);
        var token = $"snap-{Guid.NewGuid()}";
        factory.TacticusApi.ConfigurePlayerResponse(token, FakeTacticusApi.BuildPlayerResponse());
        (await client.PutAsJsonAsync("/api/v1/account/tacticus-integration", new UpdateTacticusIntegrationRequest(token, null), Ct))
            .EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var profileId = await db.Profiles.IgnoreQueryFilters()
            .Where(profile => profile.Subject == subject)
            .Select(profile => profile.Id)
            .SingleAsync(Ct);
        return (client, token, profileId);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, string? name) =>
        client.PostAsJsonAsync(url, new { name }, Ct);

    private static async Task<PersonalRosterSnapshotResponse> CreateAsync(HttpClient client, string url, string? name)
    {
        var response = await PostAsync(client, url, name);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<PersonalRosterSnapshotResponse>(Ct))!;
    }

    private static async Task<PersonalRosterSnapshotListResponse> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<PersonalRosterSnapshotListResponse>(Url, Ct))!;

    private static async Task<RosterSnapshotResponse> ReadRosterAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<RosterSnapshotResponse>($"{Url}/{id}", Ct))!;

    private async Task<DateTimeOffset> SyncedAtAsync(ProfileId profileId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        return await db.PlayerDataSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(entity => entity.Id == profileId)
            .Select(entity => entity.SyncedAt)
            .SingleAsync(Ct);
    }

    private async Task<DateTimeOffset?> LastAttemptedAtAsync(ProfileId profileId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        return await db.TacticusIntegrations.IgnoreQueryFilters()
            .Where(entity => entity.Id == profileId)
            .Select(entity => entity.TacticusSyncLastAttemptedAt)
            .SingleAsync(Ct);
    }

    private async Task SeedAsync(ProfileId profileId, IEnumerable<(RosterSnapshotSource Source, DateTimeOffset TakenAt)> snapshots)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        foreach (var (source, takenAt) in snapshots)
        {
            db.PersonalRosterSnapshots.Add(PersonalRosterSnapshot.Create(
                profileId,
                null,
                source,
                new RosterSnapshot
                {
                    TakenAt = takenAt,
                    CatalogVersion = "test",
                    Characters = [new PlayerCharacterRecord { UnitId = UnitId.From(FakeTacticusApi.CharacterUnitId) }],
                }));
        }

        await db.SaveChangesAsync(Ct);
    }

    private async Task SeedRunRecordAsync(ProfileId profileId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        db.LegendaryEventRunRecords.Add(new LegendaryEventRunRecord
        {
            Id = LegendaryEventRunRecordId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            EventId = "votanUthar",
            Run = 1,
            StartedAt = Day(0),
            ProgressAt = Day(0),
            CatalogVersion = "test",
        });
        await db.SaveChangesAsync(Ct);
    }
}
