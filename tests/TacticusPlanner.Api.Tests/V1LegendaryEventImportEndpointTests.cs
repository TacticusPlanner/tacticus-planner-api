using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.LegendaryEventPlans;
using TacticusPlanner.Api.Features.V1Import;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// Covers <c>add-legendary-event-teams</c>'s <c>v1-legendary-event-import</c> capability: the
/// <c>legendaryEventPlans</c> part of <c>POST /me/v1-import</c>, its event/lane/unit/objective resolution, the
/// per-event outcomes and issues, and the part summary. V1 blobs are written as JSON and read through the same
/// reader the live client uses.
/// </summary>
public sealed class V1LegendaryEventImportEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const int LysanderV1Id = 15;
    private const string LysanderId = "astarLysander";
    private const int DanteV1Id = 10;

    private readonly GameCatalogSnapshot catalog = factory.Services.GetRequiredService<IGameCatalogProvider>().Current;

    private GameCatalogLreView Lysander => catalog.LreViews.Single(view => view.Id == LysanderId);

    private IReadOnlyList<string> Alpha => Lysander.Alpha.AvailableUnitIds;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PartNotSelectedLeavesPlansAlone()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(TeamsJson(LysanderV1Id, Team("A", "alpha", Alpha[0])));

        var body = await ImportAsync(client, username, selected: false, onslaught: true);

        Assert.Equal(("Skipped", "not_selected"), (body.LegendaryEventPlans.Status, body.LegendaryEventPlans.Code));
        Assert.Empty(body.LegendaryEventOutcomes);
        Assert.Equal(0, (await ReadPlanAsync(client)).Revision);
    }

    [Fact]
    public async Task MissingTeamsKeysAreSkipped()
    {
        var (client, _) = await ClientAsync();

        var body = await ImportAsync(client, Configure("{}"));

        Assert.Equal(("Skipped", "missing_legendary_event_plans"), (body.LegendaryEventPlans.Status, body.LegendaryEventPlans.Code));
    }

    [Fact]
    public async Task UnreadableTeamsFailThePart()
    {
        var (client, _) = await ClientAsync();

        var body = await ImportAsync(client, Configure("""{"leTeams": {"15": {"teams": "not a list"}}}"""));

        Assert.Equal(("Failed", "invalid_legendary_event_plans"), (body.LegendaryEventPlans.Status, body.LegendaryEventPlans.Code));
        Assert.Equal(0, (await ReadPlanAsync(client)).Revision);
    }

    [Fact]
    public async Task LegacyKeysAreReadWhenTheCurrentOnesAreAbsent()
    {
        var (client, _) = await ClientAsync();
        var json = $$"""
            {
              "legendaryEvents3": {{TeamsObjectByKey(LysanderV1Id, Team("Legacy", "alpha", Alpha[0]))}},
              "legendaryEventsProgress": { "15": { "notes": "Old notes" } }
            }
            """;

        var body = await ImportAsync(client, Configure(json));

        Assert.Equal("Imported", body.LegendaryEventPlans.Status);
        var plan = await ReadPlanAsync(client);
        Assert.Equal("Legacy", Assert.Single(plan.Teams).Name);
        Assert.Equal("Old notes", plan.Notes);
    }

    [Fact]
    public async Task LegacyLaneMapsAreConvertedWhenTeamsIsEmpty()
    {
        var (client, _) = await ClientAsync();
        var objectives = Lysander.Alpha.UnitsRestrictions;
        var json = $$"""
            {"leTeams": {"15": {
              "teams": [],
              "alpha": {
                "{{objectives[0].Name}}": ["{{Alpha[0]}}", "{{Alpha[1]}}"],
                "{{objectives[1].Name}}": ["{{Alpha[1]}}", "{{Alpha[0]}}"],
                "{{objectives[2].Name}}": ["{{Alpha[2]}}"],
                "{{objectives[3].Name}}": []
              }
            } } }
            """;

        await ImportAsync(client, Configure(json));

        var plan = await ReadPlanAsync(client);
        Assert.Equal(["Team 1 - alpha", "Team 2 - alpha"], plan.Teams.Select(team => team.Name));
        Assert.Equal([objectives[0].Index, objectives[1].Index], plan.Teams[0].ObjectiveIndexes);
        Assert.Equal([Alpha[0], Alpha[1]], plan.Teams[0].MemberUnitIds);
    }

    [Fact]
    public async Task EventsAreResolvedThroughTheCatalogAndReportedOnceEach()
    {
        var (client, _) = await ClientAsync();
        var json = $$"""
            {"leTeams": {
              "{{DanteV1Id}}": {{TeamsObject(DanteV1Id, Team("Old", "alpha", "bloodDante"))}},
              "{{LysanderV1Id}}": {{TeamsObject(LysanderV1Id, Team("Melee", "alpha", Alpha[0], Alpha[1]))}}
            } }
            """;

        var body = await ImportAsync(client, Configure(json));

        Assert.Equal("Imported", body.LegendaryEventPlans.Status);
        Assert.Equal(
            [(DanteV1Id, (string?)null, "Skipped", "event_not_in_catalog"), (LysanderV1Id, LysanderId, "Imported", "imported")],
            body.LegendaryEventOutcomes.Select(outcome => (outcome.V1EventId, outcome.EventId, outcome.Status, outcome.Code)));
        Assert.Equal(1, body.LegendaryEventOutcomes[1].TeamsImported);
        var team = Assert.Single((await ReadPlanAsync(client)).Teams);
        Assert.Equal(("alpha", "Melee", 0), (team.LaneId, team.Name, team.SortOrder));
        Assert.Equal([Alpha[0], Alpha[1]], team.MemberUnitIds);
    }

    [Fact]
    public async Task ObjectiveNamesResolveAcrossV1AndCatalogSpellings()
    {
        var (client, _) = await ClientAsync();
        var restrictions = Lysander.Alpha.UnitsRestrictions;
        var minHits = restrictions.Single(restriction => restriction.Filter.Kind == "MinHits");
        var noResilient = restrictions.Single(restriction => restriction.Filter is { Kind: "Trait", Target: "Resilient", Exclude: true });
        var flying = restrictions.Single(restriction => restriction.Filter is { Kind: "Trait", Target: "Flying" });
        var team = Team("Mixed", "alpha", Alpha[0]) with
        {
            RestrictionsIds = [$"Min {minHits.Filter.Target} hits", "No Resiliant", "  FLYING ", "Not an objective"],
        };

        var body = await ImportAsync(client, Configure(TeamsJson(LysanderV1Id, team)));

        Assert.Equal(
            new[] { minHits.Index, noResilient.Index, flying.Index }.Order(),
            (await ReadPlanAsync(client)).Teams[0].ObjectiveIndexes);
        Assert.Equal([("unknown_objective", "Mixed", "Not an objective")], Issues(body));
    }

    [Fact]
    public async Task UnresolvableUnitsAreDroppedAndTheTeamKept()
    {
        var (client, _) = await ClientAsync();
        var notAllowed = Lysander.Beta.AvailableUnitIds.Except(Alpha).First();
        var team = Team("Dropped", "alpha", Alpha[0], Alpha[1], "unknownX", notAllowed, Alpha[0]);

        var body = await ImportAsync(client, Configure(TeamsJson(LysanderV1Id, team)));

        Assert.Equal([Alpha[0], Alpha[1]], (await ReadPlanAsync(client)).Teams[0].MemberUnitIds);
        Assert.Equal(
            [("unknown_unit", "Dropped", "unknownX"), ("unit_not_allowed_on_lane", "Dropped", notAllowed),
                ("duplicate_unit", "Dropped", Alpha[0])],
            Issues(body));
    }

    [Fact]
    public async Task LegacyNamesAndAliasesResolveToCatalogIds()
    {
        var named = catalog.Characters.First(character => Alpha.Contains(character.Id) && character.Id != Alpha[0]);
        var (client, _) = await ClientAsync();
        await ImportAsync(client, Configure(TeamsJson(
            LysanderV1Id, new V1LreTeam(Name: "Names", Section: "alpha", CharactersIds: [named.Name.ToUpperInvariant()]))));
        Assert.Equal([named.Id], (await ReadPlanAsync(client)).Teams[0].MemberUnitIds);

        var aliasLane = catalog.LreViews
            .SelectMany(view => new[] { ("alpha", view.Alpha), ("beta", view.Beta), ("gamma", view.Gamma) }
                .Select(lane => (View: view, LaneId: lane.Item1, Lane: lane.Item2)))
            .First(entry => entry.Lane.AvailableUnitIds.Contains("genesPatriarch"));
        var v1Id = catalog.Lres.Single(lre => lre.UnitSnowprintId == aliasLane.View.Id).Id;
        (client, _) = await ClientAsync();
        await ImportAsync(client, Configure(TeamsJson(v1Id, Team("Alias", aliasLane.LaneId, "Patermine"))));
        Assert.Equal(["genesPatriarch"], (await ReadPlanAsync(client, aliasLane.View.Id)).Teams[0].MemberUnitIds);
    }

    [Fact]
    public async Task EmptyAndUnknownLaneTeamsAreSkippedAndLongTeamsTruncated()
    {
        var (client, _) = await ClientAsync();
        var json = TeamsJson(LysanderV1Id,
            Team("Ghosts", "alpha", "unknownX"),
            Team("Nowhere", "delta", Alpha[0]),
            Team("Crowd", "alpha", [.. Alpha.Take(7)]));

        var body = await ImportAsync(client, Configure(json));

        var team = Assert.Single((await ReadPlanAsync(client)).Teams);
        Assert.Equal(Alpha.Take(5), team.MemberUnitIds);
        Assert.Equal(
            [("unknown_unit", "Ghosts", "unknownX"), ("empty_team", "Ghosts", null), ("unknown_lane", "Nowhere", "delta"),
                ("team_truncated", "Crowd", "7")],
            Issues(body));
    }

    [Fact]
    public async Task DepthIsClampedAndStoredUnderTheSyncedRun()
    {
        var (client, subject) = await ClientAsync();
        await SeedSyncedRunAsync(subject, LysanderId, 2);
        var team = Team("Deep", "alpha", Alpha[0]) with { ExpectedBattleClears = 25 };

        await ImportAsync(client, Configure(TeamsJson(LysanderV1Id, team)));

        var depth = Assert.Single((await ReadPlanAsync(client)).Teams[0].RunDepths);
        Assert.Equal((2, Lysander.Alpha.BattleIds.Count, "manual"), (depth.Run, depth.ExpectedBattleClears, depth.ExpectedBattleClearsSource));
    }

    [Fact]
    public async Task DepthWithoutASyncedRunLandsOnRunOneAndZeroDepthIsOmitted()
    {
        var (client, _) = await ClientAsync();
        var json = TeamsJson(LysanderV1Id,
            Team("Five", "alpha", Alpha[0]) with { ExpectedBattleClears = 5 },
            Team("Zero", "alpha", Alpha[1]) with { ExpectedBattleClears = 0 });

        await ImportAsync(client, Configure(json));

        var teams = (await ReadPlanAsync(client)).Teams;
        Assert.Equal((1, 5), (teams[0].RunDepths[0].Run, teams[0].RunDepths[0].ExpectedBattleClears));
        Assert.Empty(teams[1].RunDepths);
    }

    [Fact]
    public async Task IdenticalTeamsInALaneAreMergedKeepingTheFirstDepth()
    {
        var (client, _) = await ClientAsync();
        var objectives = Lysander.Alpha.UnitsRestrictions;
        var json = TeamsJson(LysanderV1Id,
            Team("First", "alpha", Alpha[0], Alpha[1]) with { ExpectedBattleClears = 7, RestrictionsIds = [objectives[0].Name] },
            Team("Second", "alpha", Alpha[1], Alpha[0]) with { ExpectedBattleClears = 9, RestrictionsIds = [objectives[1].Name] },
            Team("Other lane", "beta", Lysander.Beta.AvailableUnitIds[0]));

        var body = await ImportAsync(client, Configure(json));

        var plan = await ReadPlanAsync(client);
        Assert.Equal(["First", "Other lane"], plan.Teams.Select(team => team.Name));
        Assert.Equal([objectives[0].Index, objectives[1].Index], plan.Teams[0].ObjectiveIndexes);
        Assert.Equal(7, plan.Teams[0].RunDepths[0].ExpectedBattleClears);
        Assert.Equal(
            [("duplicate_team_merged", "Second", "First"), ("conflicting_depth_discarded", "Second", "9")],
            Issues(body));
        Assert.Equal(2, body.LegendaryEventOutcomes[0].TeamsImported);
    }

    [Fact]
    public async Task ReImportSkipsAPlanThatAlreadyHasTeams()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(TeamsJson(LysanderV1Id, Team("A", "alpha", Alpha[0])));
        await ImportAsync(client, username);
        var before = await ReadPlanAsync(client);

        var body = await ImportAsync(client, username);

        var outcome = Assert.Single(body.LegendaryEventOutcomes);
        Assert.Equal(("Skipped", "plan_already_exists"), (outcome.Status, outcome.Code));
        Assert.Equal(("Skipped", "no_legendary_event_imported"), (body.LegendaryEventPlans.Status, body.LegendaryEventPlans.Code));
        var after = await ReadPlanAsync(client);
        Assert.Equal((before.Revision, 1), (after.Revision, after.Teams.Count));
    }

    [Fact]
    public async Task ImportFillsAnExistingEmptyPlanAndCarriesNotes()
    {
        var (client, _) = await ClientAsync();
        var created = await client.PutAsJsonAsync(
            $"/api/v1/me/legendary-event-plans/{LysanderId}", new UpdateLegendaryEventPlanRequest(0, null, true), Ct);
        created.EnsureSuccessStatusCode();
        var json = $$"""
            {
              "leTeams": {{TeamsObjectByKey(LysanderV1Id, Team("A", "alpha", Alpha[0]))}},
              "leProgress": { "15": { "notes": "Save tokens for beta", "compactProgress": { "alpha": {} } } }
            }
            """;

        await ImportAsync(client, Configure(json));

        var plan = await ReadPlanAsync(client);
        Assert.Equal(("Save tokens for beta", true, 2L), (plan.Notes, plan.ShowPaidOptions, plan.Revision));
        Assert.Single(plan.Teams);
    }

    [Fact]
    public async Task MixedEventsImportThePart()
    {
        var (client, _) = await ClientAsync();
        var uthar = catalog.LreViews.Single(view => view.Id == "votanUthar");
        var utharV1Id = catalog.Lres.Single(lre => lre.UnitSnowprintId == uthar.Id).Id;
        await client.PostAsJsonAsync($"/api/v1/me/legendary-event-plans/{uthar.Id}/teams",
            new CreateLegendaryEventTeamRequest(0, "alpha", "Mine", [uthar.Alpha.AvailableUnitIds[0]], null, [], 1, null, null), Ct);
        var json = $$"""
            {"leTeams": {
              "{{DanteV1Id}}": {{TeamsObject(DanteV1Id, Team("Old", "alpha", "bloodDante"))}},
              "{{utharV1Id}}": {{TeamsObject(utharV1Id, Team("Theirs", "alpha", uthar.Alpha.AvailableUnitIds[1]))}},
              "{{LysanderV1Id}}": {{TeamsObject(LysanderV1Id, Team("New", "alpha", Alpha[0]))}}
            } }
            """;

        var body = await ImportAsync(client, Configure(json));

        Assert.Equal("Imported", body.LegendaryEventPlans.Status);
        // Outcomes come back in ascending V1 id order: Dante (10), Uthar (14), Lysander (15).
        Assert.Equal(
            ["event_not_in_catalog", "plan_already_exists", "imported"],
            body.LegendaryEventOutcomes.Select(outcome => outcome.Code));
        Assert.Equal(["Mine"], (await ReadPlanAsync(client, uthar.Id)).Teams.Select(team => team.Name));
    }

    [Fact]
    public async Task SkippedWithoutAnImportReportsNoLegendaryEventImported()
    {
        var (client, _) = await ClientAsync();

        var body = await ImportAsync(client, Configure(TeamsJson(DanteV1Id, Team("Old", "alpha", "bloodDante"))));

        Assert.Equal(("Skipped", "no_legendary_event_imported"), (body.LegendaryEventPlans.Status, body.LegendaryEventPlans.Code));
    }

    [Fact]
    public async Task GoalsAndOnslaughtPartsStillImportAlongside()
    {
        var (client, _) = await ClientAsync();
        var json = """{"onslaughtPreferences": null}""";

        var body = await ImportAsync(client, Configure(json), onslaught: true);

        Assert.Equal("missing_onslaught_progress", body.OnslaughtProgress.Code);
        Assert.Equal("missing_legendary_event_plans", body.LegendaryEventPlans.Code);
    }

    private static V1LreTeam Team(string name, string lane, params string[] units) =>
        new(Name: name, Section: lane, RestrictionsIds: [], CharSnowprintIds: [.. units]);

    private static string TeamsObject(int v1Id, params V1LreTeam[] teams) =>
        JsonSerializer.Serialize(new V1LegendaryEventTeams(v1Id, "event", [.. teams]), TacticusV1Client.WebJsonOptions);

    private static string TeamsObjectByKey(int v1Id, params V1LreTeam[] teams) =>
        $$"""{ "{{v1Id}}": {{TeamsObject(v1Id, teams)}} }""";

    private static string TeamsJson(int v1Id, params V1LreTeam[] teams) =>
        $$"""{ "leTeams": {{TeamsObjectByKey(v1Id, teams)}} }""";

    private static string Configure(string dataJson)
    {
        var data = JsonSerializer.Deserialize<V1UserData>(dataJson, TacticusV1Client.WebJsonOptions);
        return FakeTacticusV1Client.ConfigureProfile(new TacticusV1Profile(null, null)
        {
            LegendaryEvents = TacticusV1Client.ReadLegendaryEvents(data),
        });
    }

    private static List<(string Code, string TeamName, string? Value)> Issues(ImportV1ProfileResponse body) =>
        body.LegendaryEventOutcomes.SelectMany(outcome => outcome.Issues)
            .Select(issue => (issue.Code, issue.TeamName, issue.Value))
            .ToList();

    private async Task<(HttpClient Client, string Subject)> ClientAsync()
    {
        var subject = $"v1-lre-import-{Guid.NewGuid()}";
        return (await GoalsTestHelpers.CreateProvisionedClientAsync(factory, subject), subject);
    }

    private static async Task<ImportV1ProfileResponse> ImportAsync(
        HttpClient client, string username, bool selected = true, bool onslaught = false)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            new ImportV1ProfileRequest(
                username, FakeTacticusV1Client.ValidPassword,
                new ImportV1Selection(false, false, false, false, onslaught, false, LegendaryEventPlans: selected)),
            Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(Ct))!;
    }

    private static async Task<LegendaryEventPlanResponse> ReadPlanAsync(HttpClient client, string eventId = LysanderId) =>
        (await client.GetFromJsonAsync<LegendaryEventPlanResponse>($"/api/v1/me/legendary-event-plans/{eventId}", Ct))!;

    private async Task SeedSyncedRunAsync(string subject, string eventId, int run)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().Include(entity => entity.Profile)
            .FirstAsync(entity => entity.Subject == subject, Ct);
        db.PlayerDataSnapshots.Add(new PlayerDataSnapshot
        {
            Id = account.Profile!.Id,
            LreProgress = [new LreProgressRecord { Id = UnitId.From(eventId), CurrentEventRun = run }],
        });
        await db.SaveChangesAsync(Ct);
    }
}
