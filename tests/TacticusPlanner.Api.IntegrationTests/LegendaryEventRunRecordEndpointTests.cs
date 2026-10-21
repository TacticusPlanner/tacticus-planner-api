using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.LegendaryEventPlans;
using TacticusPlanner.Api.Features.TacticusIntegration;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameDomain;
using TacticusPlanner.Persistence;
using TacticusPlanner.TacticusApi.Models.Player;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>LRE Stage 7: run records captured at sync, their read, the plan list and plans outside the catalog.</summary>
public sealed class LegendaryEventRunRecordEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string Uthar = "votanUthar";
    private const string Lysander = "astarLysander";
    private const string SyncUrl = "/api/v1/me/player-data/sync";

    private readonly GameCatalogLreView uthar = factory.Services.GetRequiredService<IGameCatalogProvider>()
        .Current.LreViews.Single(view => view.Id == Uthar);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FirstSyncDuringARunOpensARecord()
    {
        var (client, token) = await CreateSyncingClientAsync();

        await SyncAsync(client, token, Lre(Uthar, run: 2, points: 120, alpha: [([0, 1, 2], 34)]));

        var record = Assert.Single((await ReadRecordsAsync(client, Uthar)).Records);
        Assert.Equal(2, record.Run);
        Assert.Equal(record.StartedAt, record.ProgressAt);
        Assert.Equal(120, record.Points);
        Assert.Equal(GameCatalogRelease.Version, record.CatalogVersion);
        var encounter = Assert.Single(record.Lanes.Alpha!);
        Assert.Equal([0, 1, 2], encounter.ObjectivesCleared);
        Assert.Equal(34, encounter.HighScore);
        Assert.Null(record.Lanes.Beta);
        Assert.Null(record.Lanes.Gamma);
        Assert.Empty(record.Teams);

        var snapshot = await ReadRosterAsync(client, Uthar, record.Id);
        Assert.Equal(record.ProgressAt, snapshot.TakenAt);
        var unit = Assert.Single(snapshot.Characters);
        Assert.Equal(FakeTacticusApi.CharacterUnitId, unit.UnitId.Value);
        Assert.Equal(35, Assert.Single(unit.Abilities).Level);
    }

    [Fact]
    public async Task ProgressWithinTheRunRefreshesTheRecord()
    {
        var (client, token) = await CreateSyncingClientAsync();
        await SyncAsync(client, token, Lre(Uthar, run: 2, alpha: [([0, 1, 2], 34)]));
        var first = Assert.Single((await ReadRecordsAsync(client, Uthar)).Records);

        await SyncAsync(client, token, Lre(Uthar, run: 2, alpha: [([0, 1, 2], 34), ([0], 30)]), rank: 13);

        var refreshed = Assert.Single((await ReadRecordsAsync(client, Uthar)).Records);
        Assert.Equal(first.Id, refreshed.Id);
        Assert.Equal(first.StartedAt, refreshed.StartedAt);
        Assert.True(refreshed.ProgressAt > first.ProgressAt);
        Assert.Equal(2, refreshed.Lanes.Alpha!.Count);
        var roster = await ReadRosterAsync(client, Uthar, refreshed.Id);
        Assert.Equal(refreshed.ProgressAt, roster.TakenAt);
        Assert.Equal((UnitRank)13, Assert.Single(roster.Characters).Rank);
    }

    [Fact]
    public async Task ASyncWithUnchangedProgressKeepsTheRecordFrozen()
    {
        var (client, token) = await CreateSyncingClientAsync();
        await SyncAsync(client, token, Lre(Uthar, run: 2, alpha: [([0, 1, 2], 34)]));
        var first = Assert.Single((await ReadRecordsAsync(client, Uthar)).Records);
        var firstRoster = await ReadRosterAsync(client, Uthar, first.Id);

        await SyncAsync(client, token, Lre(Uthar, run: 2, alpha: [([0, 1, 2], 34)]), rank: 13);

        var after = Assert.Single((await ReadRecordsAsync(client, Uthar)).Records);
        Assert.Equal(first.ProgressAt, after.ProgressAt);
        var afterRoster = await ReadRosterAsync(client, Uthar, after.Id);
        Assert.Equal(firstRoster.TakenAt, afterRoster.TakenAt);
        Assert.Equal((UnitRank)12, Assert.Single(afterRoster.Characters).Rank);
    }

    [Fact]
    public async Task TheNextRunOpensANewRecordWithItsOwnSnapshot()
    {
        var (client, token) = await CreateSyncingClientAsync();
        await SyncAsync(client, token, Lre(Uthar, run: 2, alpha: [([0, 1, 2], 34)]));
        var run2 = Assert.Single((await ReadRecordsAsync(client, Uthar)).Records);

        await SyncAsync(client, token, Lre(Uthar, run: 3, alpha: [([0], 20)]));

        var records = (await ReadRecordsAsync(client, Uthar)).Records;
        Assert.Equal([3, 2], records.Select(record => record.Run));
        Assert.Equivalent(run2, records[1]);
        var run2Roster = await ReadRosterAsync(client, Uthar, records[1].Id);
        var run3Roster = await ReadRosterAsync(client, Uthar, records[0].Id);
        Assert.Equal(run2.ProgressAt, run2Roster.TakenAt);
        Assert.Equal(records[0].ProgressAt, run3Roster.TakenAt);
    }

    [Fact]
    public async Task AResetWithTheSameRunNumberOpensANewRecord()
    {
        var (client, token) = await CreateSyncingClientAsync();
        var nine = Enumerable.Range(0, 9).Select(_ => ((IReadOnlyList<int>)[0, 1], 40)).ToList();
        await SyncAsync(client, token, Lre(Uthar, run: 1, alpha: nine));

        await SyncAsync(client, token, Lre(Uthar, run: 1, alpha: [([0, 1], 40), ([0], 10)]));

        var records = (await ReadRecordsAsync(client, Uthar)).Records;
        Assert.Equal(2, records.Count);
        Assert.Equal(2, records[0].Lanes.Alpha!.Count);
        Assert.Equal(9, records[1].Lanes.Alpha!.Count);
    }

    [Fact]
    public async Task EventsOutsideTheCatalogAndEntriesWithNoEncountersAreIgnored()
    {
        var (client, token) = await CreateSyncingClientAsync();

        await SyncAsync(client, token,
            Lre("notAnEvent", run: 1, alpha: [([0], 10)]),
            Lre(Lysander, run: 1, alpha: [], beta: null));

        Assert.Empty((await ReadRecordsAsync(client, Lysander)).Records);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/v1/me/legendary-event-plans/notAnEvent/run-records", Ct)).StatusCode);
    }

    [Fact]
    public async Task TeamsSnapshotCarriesTheRunsDepth()
    {
        var (client, token) = await CreateSyncingClientAsync();
        var members = uthar.Alpha.AvailableUnitIds.Take(3).ToList();
        var objectives = uthar.Alpha.UnitsRestrictions.Select(restriction => restriction.Index).Take(2).ToList();
        var created = await ReadAsync<LegendaryEventPlanResponse>(await client.PostAsJsonAsync(
            $"/api/v1/me/legendary-event-plans/{Uthar}/teams",
            new CreateLegendaryEventTeamRequest(0, "alpha", "Ultras", members, null, objectives, 1, 9, "manual"), Ct));
        var team = Assert.Single(created.Teams);
        await ReadAsync<LegendaryEventPlanResponse>(await client.PutAsJsonAsync(
            $"/api/v1/me/legendary-event-plans/{Uthar}/teams/{team.Id}",
            new UpdateLegendaryEventTeamRequest(created.Revision, "Ultras", members, null, objectives, 2, 7, "estimate"), Ct));

        await SyncAsync(client, token, Lre(Uthar, run: 2, alpha: [([0], 10)]));

        var snapshot = Assert.Single(Assert.Single((await ReadRecordsAsync(client, Uthar)).Records).Teams);
        Assert.Equal((team.Id, "alpha", "Ultras", 0), (snapshot.TeamId, snapshot.LaneId, snapshot.Name, snapshot.SortOrder));
        Assert.Equal(members, snapshot.MemberUnitIds);
        Assert.Equal(objectives.Order(), snapshot.ObjectiveIndexes);
        Assert.Equal((7, "estimate"), (snapshot.ExpectedBattleClears, snapshot.ExpectedBattleClearsSource));
    }

    [Fact]
    public async Task ACatalogEventWithNoRecordsReadsAsAnEmptyList()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await ReadRecordsAsync(client, Lysander);

        Assert.Equal(Lysander, response.EventId);
        Assert.Empty(response.Records);
    }

    [Fact]
    public async Task RecordsAreScopedToTheCallersProfile()
    {
        var (client, token) = await CreateSyncingClientAsync();
        await SyncAsync(client, token, Lre(Uthar, run: 1, alpha: [([0], 10)]));
        var other = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        Assert.Empty((await ReadRecordsAsync(other, Uthar)).Records);
    }

    [Fact]
    public async Task PlanListReturnsStoredPlansInEventIdOrder()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        Assert.Empty((await ReadPlansAsync(client)).Plans);

        await client.PutAsJsonAsync($"/api/v1/me/legendary-event-plans/{Uthar}",
            new UpdateLegendaryEventPlanRequest(0, null, true), Ct);
        await client.PutAsJsonAsync($"/api/v1/me/legendary-event-plans/{Lysander}",
            new UpdateLegendaryEventPlanRequest(0, "Alpha first", false), Ct);
        var other = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        await other.PutAsJsonAsync($"/api/v1/me/legendary-event-plans/{Uthar}",
            new UpdateLegendaryEventPlanRequest(0, "other", false), Ct);

        var plans = (await ReadPlansAsync(client)).Plans;
        Assert.Equal([Lysander, Uthar], plans.Select(plan => plan.EventId));
        Assert.Equivalent(
            await ReadAsync<LegendaryEventPlanResponse>(await client.GetAsync($"/api/v1/me/legendary-event-plans/{Lysander}", Ct)),
            plans[0]);
        Assert.Equal("Alpha first", plans[0].Notes);
        Assert.Single((await ReadPlansAsync(other)).Plans);
    }

    [Fact]
    public async Task AStoredPlanOutlivesItsCatalogEventButRejectsWrites()
    {
        var subject = $"lre-retired-{Guid.NewGuid()}";
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory, subject);
        const string retired = "retiredEvent";
        const string url = $"/api/v1/me/legendary-event-plans/{retired}";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
            var profileId = await db.Profiles.IgnoreQueryFilters()
                .Where(profile => profile.Subject == subject)
                .Select(profile => profile.Id)
                .SingleAsync(Ct);
            db.LegendaryEventPlans.Add(new LegendaryEventPlan
            {
                Id = LegendaryEventPlanId.From(Guid.NewGuid()),
                ProfileId = profileId,
                EventId = retired,
                CatalogVersion = "old-catalog",
                Notes = "kept",
            });
            await db.SaveChangesAsync(Ct);
        }

        var plan = await ReadAsync<LegendaryEventPlanResponse>(await client.GetAsync(url, Ct));
        Assert.Equal((retired, "old-catalog", "kept"), (plan.EventId, plan.CatalogVersion, plan.Notes));
        Assert.Contains((await ReadPlansAsync(client)).Plans, listed => listed.EventId == retired);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync(url, new UpdateLegendaryEventPlanRequest(plan.Revision, "changed", false), Ct)).StatusCode);
        Assert.Equal("kept", (await ReadAsync<LegendaryEventPlanResponse>(await client.GetAsync(url, Ct))).Notes);
    }

    [Fact]
    public async Task RecordsArePagedNewestFirstWithAnOpaqueCursor()
    {
        var (client, profileId) = await CreateProfileAsync();
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var seeded = await SeedRecordsAsync(profileId, Uthar, Enumerable.Range(0, 25).Select(day => start.AddDays(day)));

        var first = await ReadRecordsAsync(client, Uthar, "?limit=20");
        var second = await ReadRecordsAsync(client, Uthar, $"?limit=20&after={first.NextAfter}");

        Assert.Equal(20, first.Records.Count);
        Assert.NotNull(first.NextAfter);
        Assert.Equal(5, second.Records.Count);
        Assert.Null(second.NextAfter);
        Assert.Equal(
            seeded.OrderByDescending(record => record.StartedAt).Select(record => record.Id.Value),
            first.Records.Concat(second.Records).Select(record => record.Id));
        // The default page is 20.
        Assert.Equal(20, (await ReadRecordsAsync(client, Uthar)).Records.Count);
    }

    [Fact]
    public async Task EqualTimestampsAcrossPagesAreNeitherSkippedNorRepeated()
    {
        var (client, profileId) = await CreateProfileAsync();
        var sameTime = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        var seeded = await SeedRecordsAsync(profileId, Uthar, Enumerable.Repeat(sameTime, 7));

        var ids = new List<Guid>();
        string? after = null;
        do
        {
            var page = await ReadRecordsAsync(client, Uthar, after is null ? "?limit=2" : $"?limit=2&after={after}");
            ids.AddRange(page.Records.Select(record => record.Id));
            after = page.NextAfter;
        }
        while (after is not null);

        Assert.Equal(7, ids.Count);
        Assert.Equal(seeded.Select(record => record.Id.Value).ToHashSet(), ids.ToHashSet());
        // Within one timestamp, newest-first is by descending id, as the database orders uuids.
        Assert.Equal(ids.OrderDescending(GuidByteOrder.Instance), ids);
    }

    [Theory]
    [InlineData("?after=not-a-cursor", "after")]
    [InlineData("?after=djI", "after")] // base64url of "v2"
    [InlineData("?limit=0", "limit")]
    [InlineData("?limit=101", "limit")]
    [InlineData("?limit=abc", "limit")]
    public async Task InvalidPagingParametersAreRejectedNamingTheField(string query, string field)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.GetAsync($"/api/v1/me/legendary-event-plans/{Uthar}/run-records{query}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull((await response.ReadProblemAsync())["errors"]![field]);
    }

    [Fact]
    public async Task ARosterIsReadOnDemandWithAnETag()
    {
        var (client, token) = await CreateSyncingClientAsync();
        await SyncAsync(client, token, Lre(Uthar, run: 2, alpha: [([0, 1, 2], 34)]));
        var record = Assert.Single((await ReadRecordsAsync(client, Uthar)).Records);
        var url = $"/api/v1/me/legendary-event-plans/{Uthar}/run-records/{record.Id}/roster";

        var first = await client.GetAsync(url, Ct);
        first.EnsureSuccessStatusCode();
        var etag = first.Headers.ETag!;
        using var conditional = new HttpRequestMessage(HttpMethod.Get, url);
        conditional.Headers.IfNoneMatch.Add(etag);
        var unchanged = await client.SendAsync(conditional, Ct);

        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);

        // A refresh rewrites the roster, so the validator changes.
        await SyncAsync(client, token, Lre(Uthar, run: 2, alpha: [([0, 1, 2], 34), ([0], 30)]), rank: 13);
        using var stale = new HttpRequestMessage(HttpMethod.Get, url);
        stale.Headers.IfNoneMatch.Add(etag);
        var rewritten = await client.SendAsync(stale, Ct);
        Assert.Equal(HttpStatusCode.OK, rewritten.StatusCode);
        Assert.NotEqual(etag, rewritten.Headers.ETag);
    }

    [Fact]
    public async Task AForeignOrMismatchedRecordsRosterIsNotFound()
    {
        var (client, token) = await CreateSyncingClientAsync();
        await SyncAsync(client, token, Lre(Uthar, run: 2, alpha: [([0], 10)]));
        var record = Assert.Single((await ReadRecordsAsync(client, Uthar)).Records);
        var other = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var foreign = await other.GetAsync($"/api/v1/me/legendary-event-plans/{Uthar}/run-records/{record.Id}/roster", Ct);
        var otherEvent = await client.GetAsync($"/api/v1/me/legendary-event-plans/{Lysander}/run-records/{record.Id}/roster", Ct);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherEvent.StatusCode);
    }

    private async Task<(HttpClient Client, TacticusPlanner.Domain.Profiles.ProfileId ProfileId)> CreateProfileAsync()
    {
        var subject = $"lre-pages-{Guid.NewGuid()}";
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory, subject);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var profileId = await db.Profiles.IgnoreQueryFilters()
            .Where(profile => profile.Subject == subject)
            .Select(profile => profile.Id)
            .SingleAsync(Ct);
        return (client, profileId);
    }

    private async Task<List<LegendaryEventRunRecord>> SeedRecordsAsync(
        TacticusPlanner.Domain.Profiles.ProfileId profileId, string eventId, IEnumerable<DateTimeOffset> startedAts)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var records = startedAts
            .Select(startedAt => new LegendaryEventRunRecord
            {
                Id = LegendaryEventRunRecordId.From(Guid.CreateVersion7()),
                ProfileId = profileId,
                EventId = eventId,
                Run = 1,
                StartedAt = startedAt,
                ProgressAt = startedAt,
                CatalogVersion = "test",
            })
            .ToList();
        db.LegendaryEventRunRecords.AddRange(records);
        await db.SaveChangesAsync(Ct);
        return records;
    }

    /// <summary>PostgreSQL compares uuids bytewise (big-endian), unlike <see cref="Guid.CompareTo(Guid)"/>.</summary>
    private sealed class GuidByteOrder : IComparer<Guid>
    {
        public static readonly GuidByteOrder Instance = new();

        public int Compare(Guid x, Guid y) =>
            x.ToByteArray(bigEndian: true).AsSpan().SequenceCompareTo(y.ToByteArray(bigEndian: true));
    }

    private async Task<(HttpClient Client, string Token)> CreateSyncingClientAsync()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory, $"lre-runs-{Guid.NewGuid()}");
        var token = $"lre-runs-{Guid.NewGuid()}";
        factory.TacticusApi.ConfigurePlayerResponse(token, FakeTacticusApi.BuildPlayerResponse());
        var response = await client.PutAsJsonAsync(
            "/api/v1/me/tacticus-integration", new UpdateTacticusIntegrationRequest(token, null), Ct);
        response.EnsureSuccessStatusCode();
        return (client, token);
    }

    private async Task SyncAsync(HttpClient client, string token, params LegendaryEvent[] events) =>
        await SyncAsync(client, token, events, rank: 12);

    private async Task SyncAsync(HttpClient client, string token, LegendaryEvent lre, int rank) =>
        await SyncAsync(client, token, [lre], rank);

    private async Task SyncAsync(HttpClient client, string token, IReadOnlyList<LegendaryEvent> events, int rank)
    {
        var player = FakeTacticusApi.BuildPlayerResponse(rank: rank);
        player.Player.Progress.LegendaryEvents = [.. events];
        factory.TacticusApi.ConfigurePlayerResponse(token, player);

        var response = await client.PostAsync(SyncUrl, content: null, Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    private static LegendaryEvent Lre(
        string id,
        int run,
        IReadOnlyList<(IReadOnlyList<int> Cleared, int HighScore)>? alpha,
        IReadOnlyList<(IReadOnlyList<int> Cleared, int HighScore)>? beta = null,
        int points = 0)
    {
        var lanes = new List<LegendaryEventLane>();
        if (alpha is not null)
            lanes.Add(Lane(1, alpha));
        if (beta is not null)
            lanes.Add(Lane(2, beta));

        return new LegendaryEvent
        {
            Id = id,
            Lanes = lanes,
            CurrentPoints = points,
            CurrentEvent = new LegendaryEventCurrentEvent { Run = run },
        };
    }

    private static LegendaryEventLane Lane(int id, IReadOnlyList<(IReadOnlyList<int> Cleared, int HighScore)> encounters) =>
        new()
        {
            Id = id,
            Name = $"lane{id}",
            BattleConfigs = [],
            Progress = encounters
                .Select(encounter => new LegendaryEventProgress
                {
                    ObjectivesCleared = [.. encounter.Cleared],
                    HighScore = encounter.HighScore,
                    EncounterPoints = 5,
                })
                .ToList(),
        };

    private static async Task<LegendaryEventRunRecordsResponse> ReadRecordsAsync(HttpClient client, string eventId, string query = "") =>
        await ReadAsync<LegendaryEventRunRecordsResponse>(
            await client.GetAsync($"/api/v1/me/legendary-event-plans/{eventId}/run-records{query}", Ct));

    private static async Task<RosterSnapshotResponse> ReadRosterAsync(HttpClient client, string eventId, Guid recordId) =>
        await ReadAsync<RosterSnapshotResponse>(
            await client.GetAsync($"/api/v1/me/legendary-event-plans/{eventId}/run-records/{recordId}/roster", Ct));

    private static async Task<LegendaryEventPlanListResponse> ReadPlansAsync(HttpClient client) =>
        await ReadAsync<LegendaryEventPlanListResponse>(await client.GetAsync("/api/v1/me/legendary-event-plans", Ct));

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        var body = await response.Content.ReadFromJsonAsync<T>(Ct);
        Assert.NotNull(body);
        return body;
    }
}
