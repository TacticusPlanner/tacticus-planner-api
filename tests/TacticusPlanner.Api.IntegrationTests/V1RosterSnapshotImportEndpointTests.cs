using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.LegendaryEventPlans;
using TacticusPlanner.Api.Features.RosterSnapshots;
using TacticusPlanner.Api.Features.V1Import;
using TacticusPlanner.Domain.RosterSnapshots;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameDomain;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary><c>v1-roster-snapshot-import</c>: the <c>rosterSnapshots</c> part of <c>POST /account/v1-import</c>, its
/// chain reconstruction, unit mapping, limit, idempotency and outcomes. V1 blobs are written as JSON and read through
/// the same reader the live client uses.</summary>
public sealed class V1RosterSnapshotImportEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string SnapshotsUrl = "/api/v1/me/roster-snapshots";
    private const string Calgar = "ultraCalgar";
    private const string Tigurius = "ultraTigurius";

    // V1 enums: Rank (Locked = 0, Stone1 = 1, … Diamond1 = 16), Rarity (Legendary = 4), RarityStars (RedFiveStars = 10).
    private const int V1Diamond1 = 16;
    private const int V1Diamond2 = 17;

    private readonly GameCatalogSnapshot catalog = factory.Services.GetRequiredService<IGameCatalogProvider>().Current;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GameCatalogCharacter CalgarUnit => catalog.Characters.Single(unit => unit.Id == Calgar);

    /// <summary>An item that fits Calgar's second slot, with its 1-based slot id.</summary>
    private (GameCatalogEquipment Item, string SlotId) CalgarItem =>
        (catalog.Equipment.First(item => item.Type == CalgarUnit.EquipmentSlots[1]), "Slot2");

    private GameCatalogMow Mow => catalog.Mows[0];

    [Fact]
    public async Task NotSelectedLeavesSnapshotsAlone()
    {
        var client = await ClientAsync();
        var username = Configure(ChainJson(Base("Oct", Day(0), CalgarJson(V1Diamond1, 38))));

        var body = await ImportAsync(client, username, selected: false);

        Assert.Equal((ImportPartStatus.Skipped, "not_selected"), (body.RosterSnapshots.Status, body.RosterSnapshots.Code));
        Assert.Empty(body.RosterSnapshotOutcomes);
        Assert.Empty((await ListAsync(client)).Items);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"rosterSnapshots": {"diffs": []}}""")]
    public async Task AMissingKeyOrBaseIsSkipped(string json)
    {
        var client = await ClientAsync();

        var body = await ImportAsync(client, Configure(json));

        Assert.Equal((ImportPartStatus.Skipped, "missing_roster_snapshots"), (body.RosterSnapshots.Status, body.RosterSnapshots.Code));
    }

    [Fact]
    public async Task AnUnreadableKeyFailsThePart()
    {
        var client = await ClientAsync();

        var body = await ImportAsync(client, Configure("""{"rosterSnapshots": {"base": "not a snapshot"}}"""));

        Assert.Equal((ImportPartStatus.Failed, "invalid_roster_snapshots"), (body.RosterSnapshots.Status, body.RosterSnapshots.Code));
        Assert.Empty((await ListAsync(client)).Items);
    }

    [Fact]
    public async Task ATrashedEntryStillFeedsTheChain()
    {
        var client = await ClientAsync();
        var json = ChainJson(
            Base("Oct", Day(0), CalgarJson(V1Diamond1, 38)),
            Diff("Mid", Day(10), $$"""{"id": "{{Calgar}}", "rank": {{V1Diamond2}}}""", deleted: true),
            Diff("Nov", Day(20), $$"""{"id": "{{Calgar}}", "passive": 41}"""));

        var body = await ImportAsync(client, Configure(json));

        Assert.Equal(ImportPartStatus.Imported, body.RosterSnapshots.Status);
        Assert.Equal(
            [("Nov", V1RosterSnapshotOutcomeStatus.Imported), ("Mid", V1RosterSnapshotOutcomeStatus.SkippedDeleted), ("Oct", V1RosterSnapshotOutcomeStatus.Imported)],
            body.RosterSnapshotOutcomes.Select(outcome => (outcome.Name, outcome.Status)));
        var list = await ListAsync(client);
        Assert.Equal(2, list.Count);
        Assert.Equal(["Nov", "Oct"], list.Items.Select(item => item.Name));
        Assert.All(list.Items, item => Assert.Equal(RosterSnapshotSource.V1Import, item.Source));
        var nov = Assert.Single((await ReadRosterAsync(client, list.Items[0].Id)).Characters);
        Assert.Equal(UnitRank.Diamond2, nov.Rank);
        Assert.Equal([40, 41], nov.Abilities.Select(ability => ability.Level));
    }

    [Fact]
    public async Task UnitsAreMappedOntoTheV2RosterShape()
    {
        var client = await ClientAsync();
        var (item, slotId) = CalgarItem;
        var calgar = CalgarJson(V1Diamond1, 38, $$""", "equip0": {"id": "{{item.Id}}", "type": "{{item.Type}}"}, "equip0Level": 3, "equip1": {"id": "notAnItem", "type": "notAType"}, "equip1Level": 1""");
        var lockedTigurius = $$"""{"id": "{{Tigurius}}", "rank": 0, "rarity": 0, "stars": 0, "shards": 5, "mythicShards": 0, "activeAbilityLevel": 0, "passiveAbilityLevel": 0, "xpLevel": 0}""";
        var unknown = """{"id": "notAUnit", "rank": 3, "rarity": 0, "stars": 0, "shards": 0, "mythicShards": 0, "activeAbilityLevel": 1, "passiveAbilityLevel": 1, "xpLevel": 1}""";
        var lockedMow = $$"""{"id": "{{Mow.Id}}", "rarity": 0, "stars": 0, "primaryAbilityLevel": 1, "secondaryAbilityLevel": 1, "shards": 0, "mythicShards": 0, "locked": true}""";
        var json = ChainJson(Base("  Worked example  ", Day(0), $"{calgar}, {lockedTigurius}, {unknown}", lockedMow));

        var body = await ImportAsync(client, Configure(json));

        // The unknown unit and the unknown item.
        Assert.Equal(2, Assert.Single(body.RosterSnapshotOutcomes).DroppedUnits);
        var snapshot = Assert.Single((await ListAsync(client)).Items);
        Assert.Equal(("Worked example", Day(0), 1, 0), (snapshot.Name, snapshot.TakenAt, snapshot.CharacterCount, snapshot.MowCount));
        var roster = await ReadRosterAsync(client, snapshot.Id);
        Assert.Equal(V1RosterSnapshotImportService.CatalogVersion, roster.CatalogVersion);
        var unit = Assert.Single(roster.Characters);
        Assert.Equal(
            (Calgar, UnitRank.Diamond1, UnitProgression.LegendaryRedFiveStars, 45, 320L, 0L, 0L),
            (unit.UnitId.Value, unit.Rank, unit.ProgressionIndex, unit.XpLevel, unit.Shards, unit.MythicShards, unit.Xp));
        Assert.Equal(
            [(CalgarUnit.ActiveAbilityNames[0], 40), (CalgarUnit.PassiveAbilityNames[0], 38)],
            unit.Abilities.Select(ability => (ability.AbilityId, ability.Level)));
        Assert.Empty(unit.AppliedUpgradeSlots);
        Assert.Equal(new RosterEquipmentSlotResponse(slotId, item.Id, 3), Assert.Single(unit.EquippedItems));
    }

    [Fact]
    public async Task LegacyStarsAndLevelsAreRaisedToTheirFloors()
    {
        var client = await ClientAsync();
        // Legendary with Rare's stars and zero levels, as old V1 snapshots stored.
        var calgar = $$"""{"id": "{{Calgar}}", "rank": {{V1Diamond1}}, "rarity": 4, "stars": 4, "shards": 0, "mythicShards": 0, "activeAbilityLevel": 0, "passiveAbilityLevel": 0, "xpLevel": 0}""";
        var mow = $$"""{"id": "{{Mow.Id}}", "rarity": 3, "stars": 12, "primaryAbilityLevel": 0, "secondaryAbilityLevel": 7, "shards": 2, "mythicShards": 0, "locked": false}""";

        await ImportAsync(client, Configure(ChainJson(Base("Old", Day(0), calgar, mow))));

        var roster = await ReadRosterAsync(client, Assert.Single((await ListAsync(client)).Items).Id);
        var unit = Assert.Single(roster.Characters);
        Assert.Equal((UnitProgression.LegendaryRedThreeStars, 1), (unit.ProgressionIndex, unit.XpLevel));
        Assert.All(unit.Abilities, ability => Assert.Equal(1, ability.Level));
        var machine = Assert.Single(roster.Mows);
        // Epic with blue stars does not exist, so the stars fall back to Epic's minimum.
        Assert.Equal((UnitProgression.EpicRedOneStar, 0), (machine.ProgressionIndex, machine.XpLevel));
        Assert.Equal(
            [(Mow.PrimaryAbility.Name, 1), (Mow.SecondaryAbility.Name, 7)],
            machine.Abilities.Select(ability => (ability.AbilityId, ability.Level)));
    }

    [Fact]
    public async Task OnlyTheRoomUnderTheLimitIsImportedNewestFirst()
    {
        var client = await ClientAsync();
        await SeedManualAsync(18);
        var json = ChainJson(
            Base("S0", Day(0), CalgarJson(V1Diamond1, 30)),
            Enumerable.Range(1, 4).Select(index => Diff($"S{index}", Day(index), $$"""{"id": "{{Calgar}}", "passive": {{30 + index}}}""")).ToArray());

        var body = await ImportAsync(client, Configure(json));

        Assert.Equal(
            [
                ("S4", V1RosterSnapshotOutcomeStatus.Imported), ("S3", V1RosterSnapshotOutcomeStatus.Imported),
                ("S2", V1RosterSnapshotOutcomeStatus.SkippedLimit), ("S1", V1RosterSnapshotOutcomeStatus.SkippedLimit),
                ("S0", V1RosterSnapshotOutcomeStatus.SkippedLimit),
            ],
            body.RosterSnapshotOutcomes.Select(outcome => (outcome.Name, outcome.Status)));
        Assert.Equal("roster_snapshot_limit_reached", body.RosterSnapshotOutcomes[^1].Code);
        Assert.Equal(20, (await ListAsync(client)).Count);
    }

    [Fact]
    public async Task ARerunSkipsEveryImportedSnapshotEvenAfterARename()
    {
        var client = await ClientAsync();
        var json = ChainJson(
            Base("First", Day(0), CalgarJson(V1Diamond1, 30)),
            Diff("Second", Day(1), $$"""{"id": "{{Calgar}}", "passive": 31}"""),
            Diff("Third", Day(2), $$"""{"id": "{{Calgar}}", "passive": 32}"""));
        var username = Configure(json);
        await ImportAsync(client, username);
        var renamed = (await ListAsync(client)).Items[0];
        (await client.PatchAsJsonAsync($"{SnapshotsUrl}/{renamed.Id}", new RenamePersonalRosterSnapshotRequest("Mine now"), Ct))
            .EnsureSuccessStatusCode();

        var body = await ImportAsync(client, username);

        Assert.Equal((ImportPartStatus.Skipped, "no_roster_snapshot_imported"), (body.RosterSnapshots.Status, body.RosterSnapshots.Code));
        Assert.All(body.RosterSnapshotOutcomes, outcome => Assert.Equal(V1RosterSnapshotOutcomeStatus.SkippedDuplicate, outcome.Status));
        Assert.Equal(3, body.RosterSnapshotOutcomes.Count);
        Assert.Equal(3, (await ListAsync(client)).Count);
    }

    private static DateTimeOffset Day(int offset) => new DateTimeOffset(2026, 5, 1, 9, 30, 0, TimeSpan.Zero).AddDays(offset);

    private static string CalgarJson(int rank, int passive, string extra = "") =>
        $$"""{"id": "{{Calgar}}", "rank": {{rank}}, "rarity": 4, "stars": 10, "shards": 320, "mythicShards": 0, "activeAbilityLevel": 40, "passiveAbilityLevel": {{passive}}, "xpLevel": 45{{extra}}}""";

    private static string Base(string name, DateTimeOffset date, string chars, string mows = "") =>
        $$"""{"name": {{JsonSerializer.Serialize(name)}}, "dateMillisUtc": {{date.ToUnixTimeMilliseconds()}}, "chars": [{{chars}}], "mows": [{{mows}}]}""";

    private static string Diff(string name, DateTimeOffset date, string charDiffs, bool deleted = false) =>
        $$"""{"name": "{{name}}", "dateMillisUtc": {{date.ToUnixTimeMilliseconds()}}, {{(deleted ? $"\"deletedDateMillisUtc\": {date.AddDays(1).ToUnixTimeMilliseconds()}, " : "")}}"charDiffs": [{{charDiffs}}], "mowDiffs": []}""";

    private static string ChainJson(string baseSnapshot, params string[] diffs) =>
        $$$"""{"rosterSnapshots": {"base": {{{baseSnapshot}}}, "diffs": [{{{string.Join(", ", diffs)}}}]}}""";

    private string Configure(string dataJson)
    {
        var data = JsonSerializer.Deserialize<V1UserData>(dataJson, TacticusV1Client.WebJsonOptions);
        return factory.V1Client.ConfigureProfile(new TacticusV1Profile(null, null)
        {
            RosterSnapshots = TacticusV1Client.ReadRosterSnapshots(data?.RosterSnapshots),
        });
    }

    private string subject = string.Empty;

    private Task<HttpClient> ClientAsync()
    {
        subject = $"v1-snap-import-{Guid.NewGuid()}";
        return GoalsTestHelpers.CreateProvisionedClientAsync(factory, subject);
    }

    private static async Task<ImportV1ProfileResponse> ImportAsync(HttpClient client, string username, bool selected = true)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/account/v1-import",
            new ImportV1ProfileRequest(
                username, FakeTacticusV1Client.ValidPassword,
                new ImportV1Selection(false, false, false, false, !selected, false, RosterSnapshots: selected)),
            Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(Ct))!;
    }

    private static async Task<PersonalRosterSnapshotListResponse> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<PersonalRosterSnapshotListResponse>(SnapshotsUrl, Ct))!;

    private static async Task<RosterSnapshotResponse> ReadRosterAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<RosterSnapshotResponse>($"{SnapshotsUrl}/{id}", Ct))!;

    private async Task SeedManualAsync(int count)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var profileId = await db.Profiles.IgnoreQueryFilters()
            .Where(profile => profile.Subject == subject)
            .Select(profile => profile.Id)
            .SingleAsync(Ct);
        for (var index = 0; index < count; index++)
        {
            db.PersonalRosterSnapshots.Add(PersonalRosterSnapshot.Create(
                profileId, null, RosterSnapshotSource.Manual,
                new RosterSnapshot { TakenAt = Day(100 + index), CatalogVersion = "test" }));
        }

        await db.SaveChangesAsync(Ct);
    }
}
