using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TacticusPlanner.Api.Features.PlayerData;
using TacticusPlanner.Api.Features.RosterSnapshots;
using TacticusPlanner.Api.Features.TacticusIntegration;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.RosterSnapshots;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary><c>automatic-roster-snapshots</c>: the 30-day check across every kind of roster snapshot, the rolling
/// quota, the setting, Keep, and failure isolation from the sync.</summary>
public sealed class AutoRosterSnapshotTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string Url = "/api/v1/me/roster-snapshots";
    private const string SyncUrl = "/api/v1/player-data/sync";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheFirstSyncTakesABaseline()
    {
        var (client, _) = await CreateSyncingClientAsync(factory);

        await SyncAsync(client);

        var list = await ListAsync(client);
        var auto = Assert.Single(list.Items);
        Assert.Equal((RosterSnapshotSource.Auto, 1), (auto.Source, auto.CharacterCount));
        Assert.Equal((12, 1, 20, 0), (list.AutoLimit, list.AutoCount, list.Limit, list.Count));
    }

    [Fact]
    public async Task ASyncWithinTheWindowOfAnyPersonalSnapshotTakesNone()
    {
        foreach (var source in new[] { RosterSnapshotSource.Manual, RosterSnapshotSource.Auto, RosterSnapshotSource.V1Import })
        {
            var (client, profileId) = await CreateSyncingClientAsync(factory);
            await SeedAsync(profileId, [(source, DaysAgo(29))]);

            await SyncAsync(client);

            Assert.Equal(source, Assert.Single((await ListAsync(client)).Items).Source);
        }
    }

    [Fact]
    public async Task ASyncWithinTheWindowOfARunRecordRosterTakesNone()
    {
        var (client, profileId) = await CreateSyncingClientAsync(factory);
        await SeedRunRecordAsync(profileId, DaysAgo(29));

        await SyncAsync(client);

        Assert.Empty((await ListAsync(client)).Items);
    }

    [Fact]
    public async Task ASyncAfterTheWindowTakesOne()
    {
        var (client, profileId) = await CreateSyncingClientAsync(factory);
        await SeedAsync(profileId, [(RosterSnapshotSource.Manual, DaysAgo(31))]);
        await SeedRunRecordAsync(profileId, DaysAgo(31));

        await SyncAsync(client);
        await SyncAsync(client);

        var items = (await ListAsync(client)).Items;
        Assert.Equal([RosterSnapshotSource.Auto, RosterSnapshotSource.Manual], items.Select(item => item.Source));
    }

    [Fact]
    public async Task AtTheQuotaTheOldestAutoIsReplacedAndManualOnesAreKept()
    {
        var (client, profileId) = await CreateSyncingClientAsync(factory);
        await SeedAsync(profileId, [(RosterSnapshotSource.Manual, DaysAgo(400))]);
        await SeedAsync(profileId, Enumerable.Range(0, 12).Select(index => (RosterSnapshotSource.Auto, DaysAgo(390 - (index * 30)))));
        var oldestAuto = (await ListAsync(client)).Items.Where(item => item.Source == RosterSnapshotSource.Auto).Last();

        await SyncAsync(client);

        var list = await ListAsync(client);
        Assert.Equal((12, 1, 13), (list.AutoCount, list.Count, list.Items.Count));
        Assert.DoesNotContain(list.Items, item => item.Id == oldestAuto.Id);
        Assert.Equal(RosterSnapshotSource.Manual, list.Items[^1].Source);
    }

    [Fact]
    public async Task TheSnapshotCreateSyncDoesNotAlsoTakeAnAutoSnapshot()
    {
        var (client, _) = await CreateSyncingClientAsync(factory);

        (await client.PostAsJsonAsync(Url, new { name = "Mine" }, Ct)).EnsureSuccessStatusCode();

        var list = await ListAsync(client);
        Assert.Equal(RosterSnapshotSource.Manual, Assert.Single(list.Items).Source);
        Assert.Equal(0, list.AutoCount);
    }

    [Fact]
    public async Task KeepTurnsAnAutoSnapshotManual()
    {
        var (client, _) = await CreateSyncingClientAsync(factory);
        await SyncAsync(client);
        var auto = Assert.Single((await ListAsync(client)).Items);

        var response = await client.PostAsync($"{Url}/{auto.Id}/keep", null, Ct);

        response.EnsureSuccessStatusCode();
        var kept = (await response.Content.ReadFromJsonAsync<PersonalRosterSnapshotResponse>(Ct))!;
        Assert.Equal(auto with { Source = RosterSnapshotSource.Manual }, kept);
        var list = await ListAsync(client);
        Assert.Equal((1, 0), (list.Count, list.AutoCount));
    }

    [Fact]
    public async Task KeepIsRejectedAtTheManualLimit()
    {
        var (client, profileId) = await CreateSyncingClientAsync(factory);
        await SeedAsync(profileId, Enumerable.Range(0, 20).Select(index => (RosterSnapshotSource.Manual, DaysAgo(100 + index))));
        await SeedAsync(profileId, [(RosterSnapshotSource.Auto, DaysAgo(1))]);
        var auto = (await ListAsync(client)).Items[0];

        var response = await client.PostAsync($"{Url}/{auto.Id}/keep", null, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("rosterSnapshotLimitReached", (await response.ReadProblemAsync())["issueCode"]!.GetValue<string>());
        Assert.Equal(1, (await ListAsync(client)).AutoCount);
    }

    [Fact]
    public async Task KeepIsRejectedForAManualSnapshotAndUnknownIds()
    {
        var (client, profileId) = await CreateSyncingClientAsync(factory);
        await SeedAsync(profileId, [(RosterSnapshotSource.Manual, DaysAgo(1))]);
        var manual = Assert.Single((await ListAsync(client)).Items);

        var response = await client.PostAsync($"{Url}/{manual.Id}/keep", null, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadProblemAsync();
        Assert.Equal("rosterSnapshotNotAutomatic", problem["issueCode"]!.GetValue<string>());
        Assert.Equal(manual.Id, problem["current"]!["id"]!.GetValue<Guid>());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"{Url}/{Guid.NewGuid()}/keep", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task AFailingAutoStepLeavesTheSyncSuccessful()
    {
        using var failing = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPlayerDataSyncDefaultStep>();
            services.AddScoped<IPlayerDataSyncDefaultStep, PoisoningStep>();
            services.AddSingleton<PoisoningProbe>();
        }));
        var (client, profileId) = await CreateSyncingClientAsync(failing);

        var response = await client.PostAsync(SyncUrl, null, Ct);

        response.EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        Assert.True(await db.PlayerDataSnapshots.IgnoreQueryFilters().AnyAsync(entity => entity.Id == profileId, Ct));
        Assert.False(await db.PersonalRosterSnapshots.IgnoreQueryFilters().AnyAsync(entity => entity.ProfileId == profileId, Ct));
        var probe = failing.Services.GetRequiredService<PoisoningProbe>();
        Assert.True(probe.Applied);
        Assert.True(probe.Discarded);
    }

    /// <summary>Records that the failing step ran and was discarded.</summary>
    private sealed class PoisoningProbe
    {
        public bool Applied { get; set; }

        public bool Discarded { get; set; }
    }

    /// <summary>Adds a snapshot, then aborts the transaction with a failing statement.</summary>
    private sealed class PoisoningStep(PlannerDbContext db, PoisoningProbe probe) : IPlayerDataSyncDefaultStep
    {
        public async Task ApplyAsync(PlayerDataSyncWrite write, CancellationToken ct)
        {
            probe.Applied = true;
            db.PersonalRosterSnapshots.Add(PersonalRosterSnapshot.Create(
                write.ProfileId, null, RosterSnapshotSource.Auto, RosterSnapshotFactory.Create(write.Characters, write.Mows, write.SyncedAt)));
            await db.Database.ExecuteSqlRawAsync("SELECT 1 / 0", ct);
        }

        public void Discard()
        {
            probe.Discarded = true;
            foreach (var entry in db.ChangeTracker.Entries<PersonalRosterSnapshot>().ToList())
                entry.State = EntityState.Detached;
        }
    }

    private static DateTimeOffset DaysAgo(int days)
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, TimeSpan.Zero).AddDays(-days);
    }

    private static async Task SyncAsync(HttpClient client) =>
        (await client.PostAsync(SyncUrl, null, Ct)).EnsureSuccessStatusCode();

    private static async Task<PersonalRosterSnapshotListResponse> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<PersonalRosterSnapshotListResponse>(Url, Ct))!;

    /// <summary>A provisioned client of <paramref name="host"/> (the shared factory, or one derived from it, which shares
    /// its database and fake Tacticus API) with a Tacticus key that syncs one character.</summary>
    private async Task<(HttpClient Client, ProfileId ProfileId)> CreateSyncingClientAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host)
    {
        var subject = $"auto-snap-{Guid.NewGuid()}";
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, subject);
        (await client.GetAsync("/api/v1/account", Ct)).EnsureSuccessStatusCode();
        var token = $"auto-snap-{Guid.NewGuid()}";
        factory.TacticusApi.ConfigurePlayerResponse(token, FakeTacticusApi.BuildPlayerResponse());
        (await client.PutAsJsonAsync("/api/v1/account/tacticus-integration", new UpdateTacticusIntegrationRequest(token, null), Ct))
            .EnsureSuccessStatusCode();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var profileId = await db.Profiles.IgnoreQueryFilters()
            .Where(profile => profile.Subject == subject)
            .Select(profile => profile.Id)
            .SingleAsync(Ct);
        return (client, profileId);
    }

    private async Task SeedAsync(ProfileId profileId, IEnumerable<(RosterSnapshotSource Source, DateTimeOffset TakenAt)> snapshots)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        foreach (var (source, takenAt) in snapshots)
            db.PersonalRosterSnapshots.Add(PersonalRosterSnapshot.Create(profileId, null, source, Roster(takenAt)));
        await db.SaveChangesAsync(Ct);
    }

    private async Task SeedRunRecordAsync(ProfileId profileId, DateTimeOffset takenAt)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        db.LegendaryEventRunRecords.Add(new LegendaryEventRunRecord
        {
            Id = LegendaryEventRunRecordId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            EventId = "votanUthar",
            Run = 1,
            StartedAt = takenAt,
            ProgressAt = takenAt,
            CatalogVersion = "test",
            Roster = Roster(takenAt),
        });
        await db.SaveChangesAsync(Ct);
    }

    private static RosterSnapshot Roster(DateTimeOffset takenAt) => new()
    {
        TakenAt = takenAt,
        CatalogVersion = "test",
        Characters = [new PlayerCharacterRecord { UnitId = UnitId.From(FakeTacticusApi.CharacterUnitId) }],
    };
}
