using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.LegendaryEventPlans;
using TacticusPlanner.Api.Features.V1Import;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Tests;

/// <summary>Coverage for <c>v1-legendary-event-import</c> (openspec change add-legendary-event-teams): the
/// <c>legendaryEventPlans</c> part of <c>POST /me/v1-import</c>. Uses the embedded catalog's
/// <c>astarLysander</c> (V1 id 15): alpha excludes Xenos, beta excludes Imperial, gamma excludes Chaos,
/// 18 battles per lane. V1 id 10 (Dante) is not in the catalog.</summary>
public sealed class V1LegendaryEventImportEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string EventId = "astarLysander";
    private const int LysanderV1Id = 15;
    private const int DanteV1Id = 10;
    private const int UtharV1Id = 14; // votanUthar, also in the catalog

    private const string U1 = "ultraInceptorSgt";
    private const string U2 = "astarCyrus";
    private const string U3 = "admecDominus";
    private const string XenosUnit = "eldarAutarch";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ----- Part selection and source shape -----

    [Fact]
    public async Task PartNotSelectedIsSkippedAndTouchesNoPlan()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Lysander([Team("A", "alpha", [U1])])));

        var body = await ImportAsync(client, username, selected: false);

        Assert.Equal(("Skipped", "not_selected"), (body.LegendaryEventPlans.Status, body.LegendaryEventPlans.Code));
        Assert.Empty(body.LegendaryEventOutcomes);
        Assert.Empty((await GetPlanAsync(client)).Teams);
    }

    [Fact]
    public async Task NoV1TeamsIsSkippedWithMissingCode()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(V1LegendaryEventImportData.Missing());

        var body = await ImportAsync(client, username);

        Assert.Equal(("Skipped", "missing_legendary_event_plans"), (body.LegendaryEventPlans.Status, body.LegendaryEventPlans.Code));
        Assert.Empty(body.LegendaryEventOutcomes);
    }

    [Fact]
    public async Task UnreadableV1TeamsIsFailedWithInvalidCodeAndNothingIsCreated()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(V1LegendaryEventImportData.Invalid());

        var body = await ImportAsync(client, username);

        Assert.Equal(("Failed", "invalid_legendary_event_plans"), (body.LegendaryEventPlans.Status, body.LegendaryEventPlans.Code));
        Assert.Empty((await GetPlanAsync(client)).Teams);
    }

    [Fact]
    public void LegacyKeyIsReadWhenLeTeamsIsAbsent()
    {
        var legacy = Parse("""{"15":{"teams":[{"name":"A","section":"alpha","charSnowprintIds":["ultraInceptorSgt"]}]}}""");

        var data = TacticusV1Client.ReadLegendaryEvents(new V1UserData(null, null, null, LegendaryEvents3: legacy));

        Assert.True(data.IsPresent);
        var source = Assert.Single(data.Events!);
        Assert.Equal(LysanderV1Id, source.V1EventId);
        Assert.Equal("A", Assert.Single(source.Teams).Name);
    }

    [Fact]
    public void LeTeamsWinsOverTheLegacyKeyAndNotesFallBackToo()
    {
        var teams = Parse("""{"15":{"teams":[{"name":"New","section":"alpha","charSnowprintIds":["ultraInceptorSgt"]}]}}""");
        var legacy = Parse("""{"15":{"teams":[{"name":"Old","section":"alpha","charSnowprintIds":["ultraInceptorSgt"]}]}}""");
        var progress = Parse("""{"15":{"notes":"Save tokens for beta","state":"started"}}""");

        var data = TacticusV1Client.ReadLegendaryEvents(new V1UserData(
            null, null, null, LeTeams: teams, LegendaryEvents3: legacy, LegendaryEventsProgress: progress));

        var source = Assert.Single(data.Events!);
        Assert.Equal("New", Assert.Single(source.Teams).Name);
        Assert.Equal("Save tokens for beta", source.Notes);
    }

    [Fact]
    public void LegacyLaneMapsAndNonNumericKeysAreRead()
    {
        var teams = Parse("""
            {"15":{"alpha":{"Min 5 hits":["ultraInceptorSgt","astarCyrus"]},"beta":{},"gamma":{"Healer":[]}},
             "notANumber":{"teams":[]}}
            """);

        var data = TacticusV1Client.ReadLegendaryEvents(new V1UserData(null, null, null, LeTeams: teams));

        var source = Assert.Single(data.Events!);
        Assert.Empty(source.Teams);
        Assert.Equal([U1, U2], source.LegacyAlpha["Min 5 hits"]);
        Assert.Empty(source.LegacyBeta);
        Assert.Empty(source.LegacyGamma["Healer"]);
    }

    [Fact]
    public void MissingAndMalformedBlobsAreDistinguished()
    {
        var missing = TacticusV1Client.ReadLegendaryEvents(new V1UserData(null, null, null));
        var nullKey = TacticusV1Client.ReadLegendaryEvents(new V1UserData(null, null, null, LeTeams: Parse("null")));
        var malformed = TacticusV1Client.ReadLegendaryEvents(new V1UserData(
            null, null, null, LeTeams: Parse("""{"15":{"teams":"not a list"}}""")));

        Assert.False(missing.IsPresent);
        Assert.False(nullKey.IsPresent);
        Assert.True(malformed.IsPresent);
        Assert.Null(malformed.Events);
    }

    // ----- Event resolution -----

    [Fact]
    public async Task FinishedV1EventIsSkippedAndOthersStillProcess()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(
            Event(DanteV1Id, [Team("Dante", "alpha", [U1])]),
            Lysander([Team("A", "alpha", [U1])])));

        var body = await ImportAsync(client, username);

        Assert.Equal("Imported", body.LegendaryEventPlans.Status);
        Assert.Equal(2, body.LegendaryEventOutcomes.Count);
        var dante = body.LegendaryEventOutcomes[0];
        Assert.Equal((null, DanteV1Id, "Skipped", "event_not_in_catalog", 0), (dante.EventId, dante.V1EventId, dante.Status, dante.Code, dante.TeamsImported));
        var lysander = body.LegendaryEventOutcomes[1];
        Assert.Equal((EventId, LysanderV1Id, "Imported", "imported", 1), (lysander.EventId, lysander.V1EventId, lysander.Status, lysander.Code, lysander.TeamsImported));
        Assert.Equal("A", Assert.Single((await GetPlanAsync(client)).Teams).Name);
    }

    [Fact]
    public async Task ActiveV1EventCreatesTeamsOnThePlanInV1OrderWithDenseSortPerLane()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Lysander(
        [
            Team("A1", "alpha", [U1]),
            Team("B1", "beta", [XenosUnit]),
            Team("A2", "alpha", [U2]),
            Team("G1", "gamma", [U1, XenosUnit]),
        ])));

        var body = await ImportAsync(client, username);

        Assert.Equal(4, Assert.Single(body.LegendaryEventOutcomes).TeamsImported);
        var plan = await GetPlanAsync(client);
        Assert.Equal(1, plan.Revision);
        Assert.Equal(
            [("alpha", 0, "A1"), ("alpha", 1, "A2"), ("beta", 0, "B1"), ("gamma", 0, "G1")],
            plan.Teams.Select(team => (team.LaneId, team.SortOrder, team.Name)));
        Assert.Equal([U1, XenosUnit], plan.Teams.Single(team => team.Name == "G1").MemberUnitIds);
    }

    // ----- Team, unit and objective resolution -----

    [Fact]
    public async Task ObjectiveNamesResolveAcrossV1AndCatalogSpellings()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Lysander(
        [
            Team("A", "alpha", [U1], restrictions: ["Min 5 hits", "No Resiliant", "  suppressive   FIRE "]),
            Team("B", "beta", [XenosUnit], restrictions: ["Melee", "No Mechanical", "Orks"]),
        ])));

        var body = await ImportAsync(client, username);

        var outcome = Assert.Single(body.LegendaryEventOutcomes);
        Assert.Empty(outcome.Issues);
        var plan = await GetPlanAsync(client);
        Assert.Equal([1, 3, 4], plan.Teams.Single(team => team.Name == "A").ObjectiveIndexes);
        Assert.Equal([1, 2, 4], plan.Teams.Single(team => team.Name == "B").ObjectiveIndexes);
    }

    [Fact]
    public async Task UnknownObjectiveIsDroppedAndReported()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Lysander([Team("A", "alpha", [U1], restrictions: ["Flying", "Teleport"])])));

        var body = await ImportAsync(client, username);

        var issue = Assert.Single(Assert.Single(body.LegendaryEventOutcomes).Issues);
        Assert.Equal(("unknown_objective", "A", "Teleport"), (issue.Code, issue.TeamName, issue.Value));
        Assert.Equal([2], Assert.Single((await GetPlanAsync(client)).Teams).ObjectiveIndexes);
    }

    [Fact]
    public async Task UnitDroppedButTeamKept()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Lysander([Team("A", "alpha", [U1, U2, "unknownX"])])));

        var body = await ImportAsync(client, username);

        var issue = Assert.Single(Assert.Single(body.LegendaryEventOutcomes).Issues);
        Assert.Equal(("unknown_unit", "A", "unknownX"), (issue.Code, issue.TeamName, issue.Value));
        Assert.Equal([U1, U2], Assert.Single((await GetPlanAsync(client)).Teams).MemberUnitIds);
    }

    [Fact]
    public async Task UnitNotAllowedOnTheLaneIsDroppedAndReported()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Lysander([Team("A", "alpha", [U1, XenosUnit])])));

        var body = await ImportAsync(client, username);

        var issue = Assert.Single(Assert.Single(body.LegendaryEventOutcomes).Issues);
        Assert.Equal(("unit_not_allowed_on_lane", "A", XenosUnit), (issue.Code, issue.TeamName, issue.Value));
        Assert.Equal([U1], Assert.Single((await GetPlanAsync(client)).Teams).MemberUnitIds);
    }

    [Fact]
    public async Task RepeatedUnitIsKeptOnce()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Lysander([Team("A", "alpha", [U1, U2, U1])])));

        var body = await ImportAsync(client, username);

        var issue = Assert.Single(Assert.Single(body.LegendaryEventOutcomes).Issues);
        Assert.Equal(("duplicate_unit", U1), (issue.Code, issue.Value));
        Assert.Equal([U1, U2], Assert.Single((await GetPlanAsync(client)).Teams).MemberUnitIds);
    }

    [Fact]
    public async Task MoreThanFiveUnitsAreTruncated()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Lysander([Team("A", "alpha", [U1, U2, U3, "bloodDante", "custoTrajann", "templHelbrecht"])])));

        var body = await ImportAsync(client, username);

        var issue = Assert.Single(Assert.Single(body.LegendaryEventOutcomes).Issues);
        Assert.Equal(("team_truncated", "templHelbrecht"), (issue.Code, issue.Value));
        Assert.Equal(5, Assert.Single((await GetPlanAsync(client)).Teams).MemberUnitIds.Count);
    }

    [Fact]
    public async Task UnitReferencesFallBackFromSnowprintIdsToLegacyIdsToEmbeddedCharacters()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Lysander(
        [
            new V1LreTeam(null, "Ids", "alpha", null, null, ["Bellator"], null, null),
            new V1LreTeam(null, "Embedded", "alpha", null, null, null, [new V1LreTeamCharacter(null, "Bellator"), new V1LreTeamCharacter(U2, null)], null),
        ])));

        var body = await ImportAsync(client, username);

        Assert.Empty(Assert.Single(body.LegendaryEventOutcomes).Issues);
        var plan = await GetPlanAsync(client);
        Assert.Equal([U1], plan.Teams.Single(team => team.Name == "Ids").MemberUnitIds);
        Assert.Equal([U1, U2], plan.Teams.Single(team => team.Name == "Embedded").MemberUnitIds);
    }

    [Fact]
    public async Task LegacyAliasResolves()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Lysander([new V1LreTeam(null, "Cult", "beta", null, null, ["Patermine"], null, null)])));

        var body = await ImportAsync(client, username);

        Assert.Empty(Assert.Single(body.LegendaryEventOutcomes).Issues);
        Assert.Equal(["genesPatriarch"], Assert.Single((await GetPlanAsync(client)).Teams).MemberUnitIds);
    }

    [Fact]
    public async Task EmptyTeamIsSkipped()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Lysander([Team("Ghosts", "alpha", ["nobody", "noone"]), Team("Real", "alpha", [U1])])));

        var body = await ImportAsync(client, username);

        var outcome = Assert.Single(body.LegendaryEventOutcomes);
        Assert.Equal(1, outcome.TeamsImported);
        Assert.Contains(outcome.Issues, issue => issue.Code == "empty_team" && issue.TeamName == "Ghosts");
        Assert.Equal(2, outcome.Issues.Count(issue => issue.Code == "unknown_unit"));
        Assert.Equal("Real", Assert.Single((await GetPlanAsync(client)).Teams).Name);
    }

    [Fact]
    public async Task UnknownLaneIsSkipped()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Lysander([Team("Delta", "delta", [U1]), Team("A", "alpha", [U1])])));

        var body = await ImportAsync(client, username);

        var outcome = Assert.Single(body.LegendaryEventOutcomes);
        var issue = Assert.Single(outcome.Issues);
        Assert.Equal(("unknown_lane", "Delta", "delta"), (issue.Code, issue.TeamName, issue.Value));
        Assert.Equal(1, outcome.TeamsImported);
    }

    // ----- Depth -----

    [Fact]
    public async Task DepthIsClampedAndStoredUnderTheSyncedRun()
    {
        var (client, subject) = await ClientAsync();
        await SeedLreProgressAsync(subject, EventId, currentRun: 2);
        var username = Configure(Events(Lysander([Team("A", "alpha", [U1], clears: 25)])));

        await ImportAsync(client, username);

        var depth = Assert.Single(Assert.Single((await GetPlanAsync(client)).Teams).RunDepths);
        Assert.Equal((2, 18, "manual"), (depth.Run, depth.ExpectedBattleClears, depth.ExpectedBattleClearsSource));
    }

    [Fact]
    public async Task DepthWithoutASyncedRunGoesToRunOne()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Lysander([Team("A", "alpha", [U1], clears: 5), Team("B", "alpha", [U2], clears: 0)])));

        await ImportAsync(client, username);

        var plan = await GetPlanAsync(client);
        var depth = Assert.Single(plan.Teams.Single(team => team.Name == "A").RunDepths);
        Assert.Equal((1, 5, "manual"), (depth.Run, depth.ExpectedBattleClears, depth.ExpectedBattleClearsSource));
        Assert.Empty(plan.Teams.Single(team => team.Name == "B").RunDepths);
    }

    // ----- Merge -----

    [Fact]
    public async Task MergedTeamsKeepTheFirstNameAndDepthAndUnionObjectives()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Lysander(
        [
            Team("First", "alpha", [U1, U2], restrictions: ["Flying"], clears: 7),
            Team("Second", "alpha", [U2, U1], restrictions: ["Min 5 hits"], clears: 9),
            Team("Beta twin", "beta", [XenosUnit]),
        ])));

        var body = await ImportAsync(client, username);

        var outcome = Assert.Single(body.LegendaryEventOutcomes);
        Assert.Equal(2, outcome.TeamsImported);
        Assert.Contains(outcome.Issues, issue => issue.Code == "duplicate_team_merged" && issue.TeamName == "Second");
        Assert.Contains(outcome.Issues, issue => issue.Code == "conflicting_depth_discarded" && issue.TeamName == "Second" && issue.Value == "9");
        var plan = await GetPlanAsync(client);
        var merged = plan.Teams.Single(team => team.LaneId == "alpha");
        Assert.Equal("First", merged.Name);
        Assert.Equal([2, 3], merged.ObjectiveIndexes);
        Assert.Equal(7, Assert.Single(merged.RunDepths).ExpectedBattleClears);
    }

    [Fact]
    public async Task LegacyLaneMapsSynthesiseOneTeamPerRestrictionMergedByUnitSet()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Event(LysanderV1Id, [],
            alpha: new Dictionary<string, IReadOnlyList<string>>
            {
                ["Min 5 hits"] = [U1, U2],
                ["Flying"] = [U2, U1],
                ["Eviscerate"] = [U3],
            },
            gamma: new Dictionary<string, IReadOnlyList<string>> { ["Healer"] = [XenosUnit] })));

        var body = await ImportAsync(client, username);

        var outcome = Assert.Single(body.LegendaryEventOutcomes);
        Assert.Equal(3, outcome.TeamsImported);
        var plan = await GetPlanAsync(client);
        var alpha = plan.Teams.Where(team => team.LaneId == "alpha").ToList();
        Assert.Equal(2, alpha.Count);
        Assert.Equal([2, 3], alpha.Single(team => team.MemberUnitIds.Count == 2).ObjectiveIndexes);
        Assert.Equal([0], alpha.Single(team => team.MemberUnitIds.Count == 1).ObjectiveIndexes);
        Assert.Equal([1], plan.Teams.Single(team => team.LaneId == "gamma").ObjectiveIndexes);
    }

    // ----- Existing plans -----

    [Fact]
    public async Task ReImportDoesNotDuplicate()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Lysander([Team("A", "alpha", [U1])], notes: "first")));
        await ImportAsync(client, username);

        var body = await ImportAsync(client, username);

        var outcome = Assert.Single(body.LegendaryEventOutcomes);
        Assert.Equal(("Skipped", "plan_already_exists", 0), (outcome.Status, outcome.Code, outcome.TeamsImported));
        Assert.Equal(("Skipped", "no_legendary_event_imported"), (body.LegendaryEventPlans.Status, body.LegendaryEventPlans.Code));
        var plan = await GetPlanAsync(client);
        Assert.Equal(1, plan.Revision);
        Assert.Single(plan.Teams);
    }

    [Fact]
    public async Task ExistingTeamsAreNeverReplacedAndNotesStay()
    {
        var (client, _) = await ClientAsync();
        var create = await client.PostAsJsonAsync($"/api/v1/me/legendary-event-plans/{EventId}/teams",
            new CreateLegendaryEventTeamRequest(0, "alpha", "Mine", [U3], null, []), Ct);
        create.EnsureSuccessStatusCode();
        var username = Configure(Events(Lysander([Team("A", "alpha", [U1])], notes: "V1 notes")));

        var body = await ImportAsync(client, username);

        Assert.Equal("plan_already_exists", Assert.Single(body.LegendaryEventOutcomes).Code);
        var plan = await GetPlanAsync(client);
        Assert.Equal(1, plan.Revision);
        Assert.Null(plan.Notes);
        Assert.Equal("Mine", Assert.Single(plan.Teams).Name);
    }

    [Fact]
    public async Task APlanWithoutTeamsStillReceivesTheImport()
    {
        var (client, _) = await ClientAsync();
        var update = await client.PutAsJsonAsync($"/api/v1/me/legendary-event-plans/{EventId}",
            new UpdateLegendaryEventPlanRequest(0, "Mine", true), Ct);
        update.EnsureSuccessStatusCode();
        var username = Configure(Events(Lysander([Team("A", "alpha", [U1])], notes: "V1 notes")));

        var body = await ImportAsync(client, username);

        Assert.Equal("imported", Assert.Single(body.LegendaryEventOutcomes).Code);
        var plan = await GetPlanAsync(client);
        Assert.Equal(2, plan.Revision);
        Assert.Equal("V1 notes", plan.Notes);
        Assert.True(plan.ShowPaidOptions);
        Assert.Single(plan.Teams);
    }

    [Fact]
    public async Task NotesAreCarried()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Lysander([Team("A", "alpha", [U1])], notes: "Save tokens for beta")));

        await ImportAsync(client, username);

        Assert.Equal("Save tokens for beta", (await GetPlanAsync(client)).Notes);
    }

    [Fact]
    public async Task NotesAloneStillCreateThePlan()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Lysander([], notes: "Only notes")));

        var body = await ImportAsync(client, username);

        var outcome = Assert.Single(body.LegendaryEventOutcomes);
        Assert.Equal(("Imported", 0), (outcome.Status, outcome.TeamsImported));
        var plan = await GetPlanAsync(client);
        Assert.Equal("Only notes", plan.Notes);
        Assert.Empty(plan.Teams);
    }

    [Fact]
    public async Task NothingResolvedAndNoNotesIsSkippedWithoutCreatingAPlan()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Lysander([Team("Ghost", "alpha", ["nobody"])])));

        var body = await ImportAsync(client, username);

        var outcome = Assert.Single(body.LegendaryEventOutcomes);
        Assert.Equal(("Skipped", "no_teams_resolved"), (outcome.Status, outcome.Code));
        Assert.Equal(0, (await GetPlanAsync(client)).Revision);
    }

    // ----- Part summary -----

    [Fact]
    public async Task MixedResultIsImportedWithOneOutcomePerEvent()
    {
        var (client, _) = await ClientAsync();
        var first = Configure(Events(Event(UtharV1Id, [Team("U", "alpha", [U1])])));
        await ImportAsync(client, first);
        var username = Configure(Events(
            Lysander([Team("A", "alpha", [U1])]),
            Event(DanteV1Id, [Team("D", "alpha", [U1])]),
            Event(UtharV1Id, [Team("U", "alpha", [U1])])));

        var body = await ImportAsync(client, username);

        Assert.Equal("Imported", body.LegendaryEventPlans.Status);
        Assert.Equal(["imported", "event_not_in_catalog", "plan_already_exists"], body.LegendaryEventOutcomes.Select(outcome => outcome.Code));
    }

    [Fact]
    public async Task OnlySkippedEventsIsSkippedWithNoImportCode()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Event(DanteV1Id, [Team("D", "alpha", [U1])])));

        var body = await ImportAsync(client, username);

        Assert.Equal(("Skipped", "no_legendary_event_imported"), (body.LegendaryEventPlans.Status, body.LegendaryEventPlans.Code));
        Assert.Single(body.LegendaryEventOutcomes);
    }

    [Fact]
    public async Task OtherPartsAreUntouchedByTheLegendaryEventPart()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(Events(Lysander([Team("A", "alpha", [U1])])));

        var body = await ImportAsync(client, username);

        Assert.Equal("not_selected", body.Goals.Code);
        Assert.Equal("not_selected", body.OnslaughtProgress.Code);
        Assert.Empty(body.Outcomes);
    }

    // ----- Helpers -----

    private async Task<(HttpClient Client, string Subject)> ClientAsync()
    {
        var subject = $"lre-import-{Guid.NewGuid()}";
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory, subject);
        return (client, subject);
    }

    private async Task SeedLreProgressAsync(string subject, string eventId, int currentRun)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().Include(entity => entity.Profile)
            .FirstAsync(entity => entity.Subject == subject, Ct);
        db.PlayerDataSnapshots.Add(new PlayerDataSnapshot
        {
            Id = account.Profile!.Id,
            LreProgress = [new LreProgressRecord { Id = UnitId.From(eventId), CurrentEventRun = currentRun }],
        });
        await db.SaveChangesAsync(Ct);
    }

    private static string Configure(V1LegendaryEventImportData data) =>
        FakeTacticusV1Client.ConfigureProfile(new TacticusV1Profile(
            null, null, null, [], V1OnslaughtImportData.Missing(), V1CampaignEventProgressImportData.Missing(), data));

    private static V1LegendaryEventImportData Events(params V1LegendaryEventSource[] events) =>
        V1LegendaryEventImportData.Valid(events);

    private static V1LegendaryEventSource Lysander(IReadOnlyList<V1LreTeam> teams, string? notes = null) =>
        Event(LysanderV1Id, teams, notes: notes);

    private static V1LegendaryEventSource Event(
        int v1EventId,
        IReadOnlyList<V1LreTeam> teams,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? alpha = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? beta = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? gamma = null,
        string? notes = null) =>
        new(v1EventId, teams, alpha ?? Empty, beta ?? Empty, gamma ?? Empty, notes);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Empty =
        new Dictionary<string, IReadOnlyList<string>>();

    private static V1LreTeam Team(string name, string section, List<string> snowprintIds, List<string>? restrictions = null, int? clears = null) =>
        new(Guid.NewGuid().ToString(), name, section, restrictions ?? [], snowprintIds, null, null, clears);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static async Task<ImportV1ProfileResponse> ImportAsync(HttpClient client, string username, bool selected = true)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            new ImportV1ProfileRequest(
                username, FakeTacticusV1Client.ValidPassword,
                // At least one part must be selected; TacticusUserId is inert for these fixtures (no id).
                new ImportV1Selection(false, TacticusUserId: !selected, false, false, false, false, LegendaryEventPlans: selected)),
            Ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(Ct);
        Assert.NotNull(body);
        return body;
    }

    private static async Task<LegendaryEventPlanResponse> GetPlanAsync(HttpClient client)
    {
        var plan = await client.GetFromJsonAsync<LegendaryEventPlanResponse>($"/api/v1/me/legendary-event-plans/{EventId}", Ct);
        Assert.NotNull(plan);
        return plan;
    }
}
