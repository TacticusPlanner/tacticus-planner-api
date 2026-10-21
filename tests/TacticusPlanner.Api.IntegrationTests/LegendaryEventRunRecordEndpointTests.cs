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
    private const string SyncUrl = "/api/v1/tacticus-integration/player-sync";

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

        var snapshot = Assert.IsType<RosterSnapshotResponse>(record.RosterSnapshot);
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
        Assert.Equal(first.RosterSnapshot!.Id, refreshed.RosterSnapshot!.Id);
        Assert.Equal(refreshed.ProgressAt, refreshed.RosterSnapshot.TakenAt);
        Assert.Equal((UnitRank)13, Assert.Single(refreshed.RosterSnapshot.Characters).Rank);
    }

    [Fact]
    public async Task ASyncWithUnchangedProgressKeepsTheRecordFrozen()
    {
        var (client, token) = await CreateSyncingClientAsync();
        await SyncAsync(client, token, Lre(Uthar, run: 2, alpha: [([0, 1, 2], 34)]));
        var first = Assert.Single((await ReadRecordsAsync(client, Uthar)).Records);

        await SyncAsync(client, token, Lre(Uthar, run: 2, alpha: [([0, 1, 2], 34)]), rank: 13);

        var after = Assert.Single((await ReadRecordsAsync(client, Uthar)).Records);
        Assert.Equal(first.ProgressAt, after.ProgressAt);
        Assert.Equal(first.RosterSnapshot!.TakenAt, after.RosterSnapshot!.TakenAt);
        Assert.Equal((UnitRank)12, Assert.Single(after.RosterSnapshot.Characters).Rank);
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
        Assert.NotEqual(run2.RosterSnapshot!.Id, records[0].RosterSnapshot!.Id);
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
                .Where(profile => profile.Account!.Subject == subject)
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

    private static async Task<LegendaryEventRunRecordsResponse> ReadRecordsAsync(HttpClient client, string eventId) =>
        await ReadAsync<LegendaryEventRunRecordsResponse>(
            await client.GetAsync($"/api/v1/me/legendary-event-plans/{eventId}/run-records", Ct));

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
