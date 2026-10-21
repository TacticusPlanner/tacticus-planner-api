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
using TacticusApiPlayer = TacticusPlanner.TacticusApi.Models.Player;

namespace TacticusPlanner.Api.IntegrationTests;

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
    private const int UtharV1Id = 14;
    private const string UtharId = "votanUthar";

    private readonly GameCatalogSnapshot catalog = factory.Services.GetRequiredService<IGameCatalogProvider>().Current;

    private GameCatalogLreView Lysander => catalog.LreViews.Single(view => view.Id == LysanderId);

    private GameCatalogLreView Uthar => catalog.LreViews.Single(view => view.Id == UtharId);

    private IReadOnlyList<string> Alpha => Lysander.Alpha.AvailableUnitIds;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PartNotSelectedLeavesPlansAlone()
    {
        var (client, _) = await ClientAsync();
        var username = Configure(TeamsJson(LysanderV1Id, Team("A", "alpha", AlphaTeam())));

        var body = await ImportAsync(client, username, selected: false, onslaught: true);

        Assert.Equal(("Skipped", "not_selected"), (body.LegendaryEventPlans.Status, body.LegendaryEventPlans.Code));
        Assert.Empty(body.LegendaryEventOutcomes);
        Assert.Equal(0, (await ReadPlanAsync(client)).Revision);
    }

    [Fact]
    public async Task MissingTeamsAndProgressKeysAreSkipped()
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
              "legendaryEvents3": {{TeamsObjectByKey(LysanderV1Id, Team("Legacy", "alpha", AlphaTeam()))}},
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
                "{{objectives[0].Name}}": ["{{Alpha[0]}}", "{{Alpha[1]}}", "{{Alpha[2]}}"],
                "{{objectives[1].Name}}": ["{{Alpha[1]}}", "{{Alpha[2]}}", "{{Alpha[0]}}"],
                "{{objectives[2].Name}}": ["{{Alpha[3]}}", "{{Alpha[4]}}", "{{Alpha[5]}}"],
                "{{objectives[3].Name}}": []
              }
            } } }
            """;

        await ImportAsync(client, Configure(json));

        var plan = await ReadPlanAsync(client);
        Assert.Equal(["Team 1 - alpha", "Team 2 - alpha"], plan.Teams.Select(team => team.Name));
        Assert.Equal([objectives[0].Index, objectives[1].Index], plan.Teams[0].ObjectiveIndexes);
        Assert.Equal([Alpha[0], Alpha[1], Alpha[2]], plan.Teams[0].MemberUnitIds);
    }

    [Fact]
    public async Task EventsAreResolvedThroughTheCatalogAndReportedOnceEach()
    {
        var (client, _) = await ClientAsync();
        var json = $$"""
            {"leTeams": {
              "{{DanteV1Id}}": {{TeamsObject(DanteV1Id, Team("Old", "alpha", "bloodDante"))}},
              "{{LysanderV1Id}}": {{TeamsObject(LysanderV1Id, Team("Melee", "alpha", Alpha[0], Alpha[1], Alpha[2]))}}
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
        Assert.Equal([Alpha[0], Alpha[1], Alpha[2]], team.MemberUnitIds);
    }

    [Fact]
    public async Task ObjectiveNamesResolveAcrossV1AndCatalogSpellings()
    {
        var (client, _) = await ClientAsync();
        var restrictions = Lysander.Alpha.UnitsRestrictions;
        var minHits = restrictions.Single(restriction => restriction.Filter.Kind == "MinHits");
        var noResilient = restrictions.Single(restriction => restriction.Filter is { Kind: "Trait", Target: "Resilient", Exclude: true });
        var flying = restrictions.Single(restriction => restriction.Filter is { Kind: "Trait", Target: "Flying" });
        var team = Team("Mixed", "alpha", AlphaTeam()) with
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
        var team = Team("Dropped", "alpha", Alpha[0], Alpha[1], "unknownX", notAllowed, Alpha[0], Alpha[2]);

        var body = await ImportAsync(client, Configure(TeamsJson(LysanderV1Id, team)));

        Assert.Equal([Alpha[0], Alpha[1], Alpha[2]], (await ReadPlanAsync(client)).Teams[0].MemberUnitIds);
        Assert.Equal(
            [("unknown_unit", "Dropped", "unknownX"), ("unit_not_allowed_on_lane", "Dropped", notAllowed),
                ("duplicate_unit", "Dropped", Alpha[0])],
            Issues(body));
    }

    [Fact]
    public async Task LegacyNamesAndAliasesResolveToCatalogIds()
    {
        var named = catalog.Characters.Where(character => Alpha.Contains(character.Id) && character.Id != Alpha[0]).Take(3).ToList();
        var (client, _) = await ClientAsync();
        await ImportAsync(client, Configure(TeamsJson(LysanderV1Id, new V1LreTeam(
            Name: "Names", Section: "alpha", CharactersIds: [.. named.Select(character => character.Name.ToUpperInvariant())]))));
        Assert.Equal(named.Select(character => character.Id), (await ReadPlanAsync(client)).Teams[0].MemberUnitIds);

        var aliasLane = catalog.LreViews
            .SelectMany(view => new[] { ("alpha", view.Alpha), ("beta", view.Beta), ("gamma", view.Gamma) }
                .Select(lane => (View: view, LaneId: lane.Item1, Lane: lane.Item2)))
            .First(entry => entry.Lane.AvailableUnitIds.Contains("genesPatriarch"));
        var v1Id = catalog.Lres.Single(lre => lre.UnitSnowprintId == aliasLane.View.Id).Id;
        (client, _) = await ClientAsync();
        var others = aliasLane.Lane.AvailableUnitIds.Where(unitId => unitId != "genesPatriarch").Take(2).ToList();
        await ImportAsync(client, Configure(TeamsJson(v1Id, Team("Alias", aliasLane.LaneId, ["Patermine", .. others]))));
        Assert.Equal(["genesPatriarch", .. others], (await ReadPlanAsync(client, aliasLane.View.Id)).Teams[0].MemberUnitIds);
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
    public async Task TeamsLeftWithFewerThanThreeUnitsAreSkippedAsTooSmall()
    {
        var (client, _) = await ClientAsync();
        var notAllowed = Lysander.Beta.AvailableUnitIds.Except(Alpha).First();
        var json = TeamsJson(LysanderV1Id,
            Team("Pair", "alpha", Alpha[0], Alpha[1]),
            Team("Trimmed", "alpha", Alpha[2], notAllowed, Alpha[3]),
            Team("Solo", "alpha", Alpha[4], "unknownX"),
            Team("Trio", "alpha", AlphaTeam(3)));

        var body = await ImportAsync(client, Configure(json));

        var team = Assert.Single((await ReadPlanAsync(client)).Teams);
        Assert.Equal("Trio", team.Name);
        Assert.Equal(AlphaTeam(3), team.MemberUnitIds);
        Assert.Equal(1, Assert.Single(body.LegendaryEventOutcomes).TeamsImported);
        Assert.Equal(
            [("team_too_small", "Pair", "2"), ("unit_not_allowed_on_lane", "Trimmed", notAllowed),
                ("team_too_small", "Trimmed", "2"), ("unknown_unit", "Solo", "unknownX"), ("team_too_small", "Solo", "1")],
            Issues(body));
    }

    [Fact]
    public async Task DepthIsClampedAndStoredUnderTheSyncedRun()
    {
        var (client, subject) = await ClientAsync();
        await SeedSyncedRunAsync(subject, LysanderId, 2);
        var team = Team("Deep", "alpha", AlphaTeam()) with { ExpectedBattleClears = 25 };

        await ImportAsync(client, Configure(TeamsJson(LysanderV1Id, team)));

        var depth = Assert.Single((await ReadPlanAsync(client)).Teams[0].RunDepths);
        Assert.Equal((2, Lysander.Alpha.BattleIds.Count, "manual"), (depth.Run, depth.ExpectedBattleClears, depth.ExpectedBattleClearsSource));
    }

    [Fact]
    public async Task DepthWithoutASyncedRunLandsOnRunOneAndZeroDepthIsOmitted()
    {
        var (client, _) = await ClientAsync();
        var json = TeamsJson(LysanderV1Id,
            Team("Five", "alpha", AlphaTeam()) with { ExpectedBattleClears = 5 },
            Team("Zero", "alpha", AlphaTeam(3)) with { ExpectedBattleClears = 0 });

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
            Team("First", "alpha", Alpha[0], Alpha[1], Alpha[2]) with { ExpectedBattleClears = 7, RestrictionsIds = [objectives[0].Name] },
            Team("Second", "alpha", Alpha[1], Alpha[2], Alpha[0]) with { ExpectedBattleClears = 9, RestrictionsIds = [objectives[1].Name] },
            Team("Other lane", "beta", [.. Lysander.Beta.AvailableUnitIds.Take(3)]));

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
        var username = Configure(TeamsJson(LysanderV1Id, Team("A", "alpha", AlphaTeam())));
        await ImportAsync(client, username);
        var before = await ReadPlanAsync(client);

        var body = await ImportAsync(client, username);

        var outcome = Assert.Single(body.LegendaryEventOutcomes);
        Assert.Equal(("Skipped", "plan_already_exists"), (outcome.Status, outcome.Code));
        Assert.Equal(("Skipped", "legendary_events_skipped"), (body.LegendaryEventPlans.Status, body.LegendaryEventPlans.Code));
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
              "leTeams": {{TeamsObjectByKey(LysanderV1Id, Team("A", "alpha", AlphaTeam()))}},
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
            new CreateLegendaryEventTeamRequest(0, "alpha", "Mine", [.. uthar.Alpha.AvailableUnitIds.Take(3)], null, [], 1, null, null), Ct);
        var json = $$"""
            {"leTeams": {
              "{{DanteV1Id}}": {{TeamsObject(DanteV1Id, Team("Old", "alpha", "bloodDante"))}},
              "{{utharV1Id}}": {{TeamsObject(utharV1Id, Team("Theirs", "alpha", [.. uthar.Alpha.AvailableUnitIds.Skip(1).Take(3)]))}},
              "{{LysanderV1Id}}": {{TeamsObject(LysanderV1Id, Team("New", "alpha", AlphaTeam()))}}
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
    public async Task SkippedEventsWithoutAnImportReportLegendaryEventsSkipped()
    {
        var (client, _) = await ClientAsync();

        var body = await ImportAsync(client, Configure(TeamsJson(DanteV1Id, Team("Old", "alpha", "bloodDante"))));

        Assert.Equal(("Skipped", "legendary_events_skipped"), (body.LegendaryEventPlans.Status, body.LegendaryEventPlans.Code));
    }

    [Fact]
    public async Task NoResolvableTeamsReportNoLegendaryEventImported()
    {
        var (client, _) = await ClientAsync();

        var body = await ImportAsync(client, Configure(TeamsJson(LysanderV1Id, Team("Ghosts", "alpha", "unknownX"))));

        Assert.Equal(("Skipped", "no_legendary_event_imported"), (body.LegendaryEventPlans.Status, body.LegendaryEventPlans.Code));
        Assert.Equal("No V1 Legendary Event had teams to import.", body.LegendaryEventPlans.Message);
    }

    [Fact]
    public async Task NullEntriesInV1TeamsAreSkipped()
    {
        var (client, _) = await ClientAsync();
        var json = $$"""
            {"leTeams": {"15": {"teams": [
              null,
              {"name": "Holes", "section": "alpha", "charSnowprintIds": [null, "{{Alpha[0]}}", "{{Alpha[1]}}", "{{Alpha[2]}}"], "restrictionsIds": [null]},
              {"name": "Embedded", "section": "alpha", "characters": [
                null, {"snowprintId": "{{Alpha[3]}}"}, {"snowprintId": "{{Alpha[4]}}"}, {"snowprintId": "{{Alpha[5]}}"}]}
            ] } } }
            """;

        var body = await ImportAsync(client, Configure(json));

        Assert.Equal("Imported", body.LegendaryEventPlans.Status);
        var plan = await ReadPlanAsync(client);
        Assert.Equal([AlphaTeam(), AlphaTeam(3)], plan.Teams.Select(team => team.MemberUnitIds));
        Assert.Equal(
            [("unknown_unit", "Holes", null), ("unknown_objective", "Holes", null), ("unknown_unit", "Embedded", "")],
            Issues(body));
    }

    [Fact]
    public async Task NullEntriesInLegacyLaneMapsAreSkipped()
    {
        var (client, _) = await ClientAsync();
        var objectives = Lysander.Alpha.UnitsRestrictions;
        var json = $$"""
            {"leTeams": {"15": {
              "teams": [null],
              "alpha": {
                "{{objectives[0].Name}}": [null, "{{Alpha[0]}}", "{{Alpha[1]}}", "{{Alpha[2]}}"], "{{objectives[1].Name}}": null
              }
            } } }
            """;

        var body = await ImportAsync(client, Configure(json));

        Assert.Equal("Imported", body.LegendaryEventPlans.Status);
        var team = Assert.Single((await ReadPlanAsync(client)).Teams);
        Assert.Equal(AlphaTeam(), team.MemberUnitIds);
        Assert.Equal([objectives[0].Index], team.ObjectiveIndexes);
    }

    [Fact]
    public async Task ExistingV2NotesAreNeverOverwritten()
    {
        var (client, _) = await ClientAsync();
        var created = await client.PutAsJsonAsync(
            $"/api/v1/me/legendary-event-plans/{LysanderId}", new UpdateLegendaryEventPlanRequest(0, "My V2 notes", false), Ct);
        created.EnsureSuccessStatusCode();
        var json = $$"""
            {
              "leTeams": {{TeamsObjectByKey(LysanderV1Id, Team("A", "alpha", AlphaTeam()))}},
              "leProgress": { "15": { "notes": "V1 notes" } }
            }
            """;

        var body = await ImportAsync(client, Configure(json));

        var plan = await ReadPlanAsync(client);
        Assert.Equal("My V2 notes", plan.Notes);
        Assert.Single(plan.Teams);
        Assert.Equal([("existing_notes_kept", null, null)], Issues(body));
    }

    [Fact]
    public async Task LongNotesAreTruncatedWithoutSplittingASurrogatePair()
    {
        var (client, _) = await ClientAsync();
        // The emoji (a surrogate pair) straddles the 2000-char limit, so the cut lands before it.
        var notes = new string('a', 1999) + "\uD83D\uDE00" + "tail";
        var json = $$"""
            {
              "leTeams": {{TeamsObjectByKey(LysanderV1Id, Team("A", "alpha", AlphaTeam()))}},
              "leProgress": { "15": { "notes": "{{notes}}" } }
            }
            """;

        var body = await ImportAsync(client, Configure(json));

        Assert.Equal(new string('a', 1999), (await ReadPlanAsync(client)).Notes);
        Assert.Equal([("notes_truncated", null, "2005")], Issues(body));
    }

    [Fact]
    public async Task PlayerDataIsSyncedBeforeTheImportSoDepthsLandOnTheCurrentRun()
    {
        var (client, _) = await ClientAsync();
        var apiKey = $"lre-run-{Guid.NewGuid()}";
        var player = FakeTacticusApi.BuildPlayerResponse();
        player.Player.Progress.LegendaryEvents =
        [
            new TacticusApiPlayer.LegendaryEvent { Id = LysanderId, Lanes = [], CurrentEvent = new() { Run = 3 } },
        ];
        factory.TacticusApi.ConfigurePlayerResponse(apiKey, player);
        var data = JsonSerializer.Deserialize<V1UserData>(
            TeamsJson(LysanderV1Id, Team("Deep", "alpha", AlphaTeam()) with { ExpectedBattleClears = 4 }),
            TacticusV1Client.WebJsonOptions);
        var username = factory.V1Client.ConfigureProfile(new TacticusV1Profile(apiKey, null)
        {
            LegendaryEvents = TacticusV1Client.ReadLegendaryEvents(data),
        });

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            new ImportV1ProfileRequest(
                username, FakeTacticusV1Client.ValidPassword,
                new ImportV1Selection(true, false, false, false, false, false, LegendaryEventPlans: true)),
            Ct);
        response.EnsureSuccessStatusCode();

        var depth = Assert.Single((await ReadPlanAsync(client)).Teams[0].RunDepths);
        Assert.Equal((3, 4), (depth.Run, depth.ExpectedBattleClears));
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

    [Fact]
    public async Task OverviewIsImportedWithAClamp()
    {
        var (client, _) = await ClientAsync();
        var json = """
            {"leProgress": {"14": {"overview": {
              "1": { "regularMissions": 10, "premiumMissions": 0, "bundle": 0 },
              "2": { "regularMissions": 12, "premiumMissions": 0, "bundle": 1, "ohSoCloseShards": 25 },
              "3": { "regularMissions": 0, "premiumMissions": 0, "bundle": 0, "ohSoCloseShards": 0 },
              "4": { "regularMissions": 1 }
            } } } }
            """;

        var body = await ImportAsync(client, Configure(json));

        var outcome = Assert.Single(body.LegendaryEventOutcomes);
        Assert.Equal((UtharId, "Imported", "inputs_imported", 0, 2), (outcome.EventId, outcome.Status, outcome.Code,
            outcome.TeamsImported, outcome.RunInputsImported));
        Assert.Equal([("run_input_clamped", null, "2:regularMissions:12"), ("unknown_run", null, "4")], Issues(body));
        Assert.Equal(
            [(1, 10, 0, false, 0), (2, 10, 0, true, 25)],
            (await ReadPlanAsync(client, UtharId)).Runs.Select(run =>
                (run.Run, run.RegularMissions, run.PremiumMissions, run.BundlePurchased, run.CloseShards)));
    }

    [Fact]
    public async Task RunInputsReachAPlanThatAlreadyHasTeams()
    {
        var (client, _) = await ClientAsync();
        await ImportAsync(client, Configure(TeamsJson(LysanderV1Id, Team("A", "alpha", AlphaTeam()))));
        var json = $$"""
            {
              "leTeams": {{TeamsObjectByKey(LysanderV1Id, Team("A", "alpha", AlphaTeam()))}},
              "leProgress": { "15": { "overview": { "1": { "regularMissions": 7, "premiumMissions": 0, "bundle": 0 } } } }
            }
            """;

        var body = await ImportAsync(client, Configure(json));

        var outcome = Assert.Single(body.LegendaryEventOutcomes);
        Assert.Equal(("Imported", "inputs_imported", 0, 1), (outcome.Status, outcome.Code, outcome.TeamsImported, outcome.RunInputsImported));
        Assert.Equal("Imported", body.LegendaryEventPlans.Status);
        var plan = await ReadPlanAsync(client);
        Assert.Single(plan.Teams);
        Assert.Equal(7, Assert.Single(plan.Runs).RegularMissions);
    }

    [Fact]
    public async Task ExistingV2RunInputsAreKept()
    {
        var (client, _) = await ClientAsync();
        await client.PutAsJsonAsync($"/api/v1/me/legendary-event-plans/{LysanderId}/runs/1",
            new UpdateLegendaryEventRunInputsRequest(0, 3, 0, false, 0), Ct);
        var json = """
            {"leProgress": {"15": {"overview": {
              "1": { "regularMissions": 10, "premiumMissions": 0, "bundle": 0 },
              "2": { "regularMissions": 5, "premiumMissions": 0, "bundle": 0 }
            } } } }
            """;

        var body = await ImportAsync(client, Configure(json));

        Assert.Equal([("existing_run_inputs_kept", null, "1")], Issues(body));
        Assert.Equal([(1, 3), (2, 5)],
            (await ReadPlanAsync(client)).Runs.Select(run => (run.Run, run.RegularMissions)));
    }

    [Fact]
    public async Task PaidOptionsAreCarriedOnANewPlanOnly()
    {
        var (client, _) = await ClientAsync();
        await client.PutAsJsonAsync($"/api/v1/me/legendary-event-plans/{LysanderId}",
            new UpdateLegendaryEventPlanRequest(0, null, false), Ct);
        var json = $$"""
            {
              "leTeams": {
                "{{UtharV1Id}}": {{TeamsObject(UtharV1Id, Team("U", "alpha", [.. Uthar.Alpha.AvailableUnitIds.Take(3)]))}},
                "{{LysanderV1Id}}": {{TeamsObject(LysanderV1Id, Team("L", "alpha", AlphaTeam()))}}
              },
              "leSettings": { "showP2POptions": true }
            }
            """;

        await ImportAsync(client, Configure(json));

        Assert.True((await ReadPlanAsync(client, UtharId)).ShowPaidOptions);
        Assert.False((await ReadPlanAsync(client)).ShowPaidOptions);
    }

    [Fact]
    public async Task AnAbsentPaidOptionsSettingKeepsTheV2Default()
    {
        var (client, _) = await ClientAsync();

        await ImportAsync(client, Configure(TeamsJson(LysanderV1Id, Team("L", "alpha", AlphaTeam()))));

        Assert.False((await ReadPlanAsync(client)).ShowPaidOptions);
    }

    [Fact]
    public async Task CompactStatusesAreImportedAsAnnotations()
    {
        var (client, _) = await ClientAsync();
        var flying = Lysander.Alpha.UnitsRestrictions.Single(restriction => restriction.Name == "Flying");
        var json = """
            {"leProgress": {"15": {"compactProgress": {"alpha": {
              "Flying": { "states": [], "statuses": { "6": 3, "7": 1, "9": 4 } },
              "_defeatAll": { "states": [], "statuses": { "8": 2 } },
              "_killPoints": { "states": [], "statuses": { "8": 3 } },
              "_highScore": { "states": [], "statuses": [0, 2] }
            } } } } }
            """;

        var body = await ImportAsync(client, Configure(json));

        var outcome = Assert.Single(body.LegendaryEventOutcomes);
        Assert.Equal(("Imported", "inputs_imported", 3), (outcome.Status, outcome.Code, outcome.AnnotationsImported));
        Assert.Equal(
            [("alpha", 1, 0, "maybeClear"), ("alpha", 6, flying.Index + 1, "stopHere"), ("alpha", 8, 0, "stopHere")],
            (await ReadPlanAsync(client)).Annotations.Select(annotation =>
                (annotation.LaneId, annotation.BattleIndex, annotation.ObjectiveId, annotation.Status)));
    }

    [Fact]
    public async Task CompactBlockedStateWithoutAStatusIsImportedAsStopHere()
    {
        var (client, _) = await ClientAsync();
        var flying = Lysander.Alpha.UnitsRestrictions.Single(restriction => restriction.Name == "Flying");
        var json = """
            {"leProgress": {"15": {"compactProgress": {"alpha": {
              "_defeatAll": { "states": [0, 2] },
              "Flying": { "states": { "3": 2, "4": 2 }, "statuses": { "4": 1 } }
            } } } } }
            """;

        await ImportAsync(client, Configure(json));

        Assert.Equal(
            [("alpha", 1, 0, "stopHere"), ("alpha", 3, flying.Index + 1, "stopHere")],
            (await ReadPlanAsync(client)).Annotations.Select(annotation =>
                (annotation.LaneId, annotation.BattleIndex, annotation.ObjectiveId, annotation.Status)));
    }

    [Fact]
    public async Task LegacyBlockedStateIsImportedAsStopHere()
    {
        var (client, _) = await ClientAsync();
        var json = """
            {"legendaryEventsProgress": {"15": {"battlesProgress": [
              { "trackId": "beta", "battleIndex": 2, "requirements": [
                { "id": "_defeatAll", "state": 2 },
                { "id": "_killPoints", "state": 1 }
              ] },
              { "trackId": "gamma", "battleIndex": 4, "requirements": [ { "id": "_defeatAll", "state": 0, "status": 2 } ] }
            ] } } }
            """;

        await ImportAsync(client, Configure(json));

        Assert.Equal(
            [("beta", 2, 0, "stopHere"), ("gamma", 4, 0, "maybeClear")],
            (await ReadPlanAsync(client)).Annotations.Select(annotation =>
                (annotation.LaneId, annotation.BattleIndex, annotation.ObjectiveId, annotation.Status)));
    }

    [Fact]
    public async Task UnresolvableMarksAreSkippedAndReported()
    {
        var (client, _) = await ClientAsync();
        var json = """
            {"leProgress": {"15": {"compactProgress": {
              "alpha": {
                "Psyker": { "statuses": { "3": 3, "4": 2 } },
                "_defeatAll": { "statuses": { "18": 3, "2": 3 } }
              },
              "delta": { "_defeatAll": { "statuses": { "1": 3 } } }
            } } } }
            """;

        var body = await ImportAsync(client, Configure(json));

        Assert.Equal(
            [("unknown_objective", null, "alpha:Psyker"), ("unknown_battle", null, "alpha:18"), ("unknown_lane", null, "delta")],
            Issues(body));
        Assert.Equal(1, body.LegendaryEventOutcomes[0].AnnotationsImported);
    }

    [Fact]
    public async Task ExistingV2AnnotationsAreKept()
    {
        var (client, _) = await ClientAsync();
        var flying = Lysander.Alpha.UnitsRestrictions.Single(restriction => restriction.Name == "Flying");
        await client.PutAsJsonAsync($"/api/v1/me/legendary-event-plans/{LysanderId}/annotations",
            new UpdateLegendaryEventAnnotationsRequest(0, "alpha", 6, [flying.Index + 1], "maybeClear"), Ct);
        var json = """
            {"leProgress": {"15": {"compactProgress": {"alpha": { "Flying": { "statuses": { "6": 3 } } } } } } }
            """;

        var body = await ImportAsync(client, Configure(json));

        Assert.Equal([("existing_annotations_kept", null, "1")], Issues(body));
        Assert.Equal(("Skipped", "no_legendary_event_imported"), (body.LegendaryEventOutcomes[0].Status, body.LegendaryEventOutcomes[0].Code));
        Assert.Equal("maybeClear", Assert.Single((await ReadPlanAsync(client)).Annotations).Status);
    }

    [Fact]
    public async Task ProgressWithoutTeamsIsNotMissing()
    {
        var (client, _) = await ClientAsync();
        var json = """
            {"leProgress": {"13": {"overview": {"1": { "regularMissions": 4, "premiumMissions": 0, "bundle": 0 } } } } }
            """;

        var body = await ImportAsync(client, Configure(json));

        Assert.Equal("Imported", body.LegendaryEventPlans.Status);
        var outcome = Assert.Single(body.LegendaryEventOutcomes);
        Assert.Equal((13, "tauFarsight", "inputs_imported", 0), (outcome.V1EventId, outcome.EventId, outcome.Code, outcome.TeamsImported));
    }

    [Fact]
    public async Task EventsAreTheUnionOfTheTeamsAndProgressKeys()
    {
        var (client, _) = await ClientAsync();
        var json = $$"""
            {
              "leTeams": {{TeamsObjectByKey(LysanderV1Id, Team("A", "alpha", AlphaTeam()))}},
              "leProgress": {
                "13": { "overview": { "1": { "regularMissions": 4, "premiumMissions": 0, "bundle": 0 } } },
                "16": null,
                "notAnEvent": { "notes": "ignored" }
              }
            }
            """;

        var body = await ImportAsync(client, Configure(json));

        Assert.Equal([13, 15], body.LegendaryEventOutcomes.Select(outcome => outcome.V1EventId));
        Assert.Equal(["inputs_imported", "imported"], body.LegendaryEventOutcomes.Select(outcome => outcome.Code));
    }

    [Fact]
    public async Task ReImportOfEverythingSkipsWithPlanAlreadyExists()
    {
        var (client, _) = await ClientAsync();
        var json = $$"""
            {
              "leTeams": {{TeamsObjectByKey(LysanderV1Id, Team("A", "alpha", AlphaTeam()))}},
              "leProgress": { "15": {
                "notes": "Save tokens for beta",
                "overview": { "1": { "regularMissions": 7, "premiumMissions": 0, "bundle": 0 } },
                "compactProgress": { "alpha": { "_defeatAll": { "statuses": { "3": 2 } } } }
              } }
            }
            """;
        var username = Configure(json);
        await ImportAsync(client, username);
        var before = await ReadPlanAsync(client);

        var body = await ImportAsync(client, username);

        var outcome = Assert.Single(body.LegendaryEventOutcomes);
        Assert.Equal(("Skipped", "plan_already_exists"), (outcome.Status, outcome.Code));
        var after = await ReadPlanAsync(client);
        Assert.Equal((before.Revision, 1, 1, 1), (after.Revision, after.Teams.Count, after.Runs.Count, after.Annotations.Count));
    }

    [Fact]
    public async Task NotesReachAPlanWithTeamsButNoNotes()
    {
        var (client, _) = await ClientAsync();
        await ImportAsync(client, Configure(TeamsJson(LysanderV1Id, Team("A", "alpha", AlphaTeam()))));
        var json = $$"""
            {
              "leTeams": {{TeamsObjectByKey(LysanderV1Id, Team("A", "alpha", AlphaTeam()))}},
              "leProgress": { "15": { "notes": "Save tokens for beta" } }
            }
            """;

        var body = await ImportAsync(client, Configure(json));

        Assert.Equal(("Imported", "inputs_imported"), (body.LegendaryEventOutcomes[0].Status, body.LegendaryEventOutcomes[0].Code));
        var plan = await ReadPlanAsync(client);
        Assert.Equal(("Save tokens for beta", 1), (plan.Notes, plan.Teams.Count));
    }

    [Fact]
    public async Task AnUnreadableProgressEntryOnlyDropsItsInputs()
    {
        var (client, _) = await ClientAsync();
        var json = $$"""
            {
              "leTeams": {{TeamsObjectByKey(LysanderV1Id, Team("A", "alpha", AlphaTeam()))}},
              "leProgress": { "15": { "notes": 7, "overview": "broken" } }
            }
            """;

        var body = await ImportAsync(client, Configure(json));

        Assert.Equal("imported", Assert.Single(body.LegendaryEventOutcomes).Code);
        var plan = await ReadPlanAsync(client);
        Assert.Null(plan.Notes);
        Assert.Empty(plan.Runs);
    }

    /// <summary>Three consecutive alpha units from <paramref name="start"/>: the smallest team that imports.</summary>
    private string[] AlphaTeam(int start = 0) => [.. Alpha.Skip(start).Take(3)];

    private static V1LreTeam Team(string name, string lane, params string[] units) =>
        new(Name: name, Section: lane, RestrictionsIds: [], CharSnowprintIds: [.. units]);

    private static string TeamsObject(int v1Id, params V1LreTeam[] teams) =>
        JsonSerializer.Serialize(new V1LegendaryEventTeams(v1Id, "event", [.. teams]), TacticusV1Client.WebJsonOptions);

    private static string TeamsObjectByKey(int v1Id, params V1LreTeam[] teams) =>
        $$"""{ "{{v1Id}}": {{TeamsObject(v1Id, teams)}} }""";

    private static string TeamsJson(int v1Id, params V1LreTeam[] teams) =>
        $$"""{ "leTeams": {{TeamsObjectByKey(v1Id, teams)}} }""";

    private string Configure(string dataJson)
    {
        var data = JsonSerializer.Deserialize<V1UserData>(dataJson, TacticusV1Client.WebJsonOptions);
        return factory.V1Client.ConfigureProfile(new TacticusV1Profile(null, null)
        {
            LegendaryEvents = TacticusV1Client.ReadLegendaryEvents(data),
        });
    }

    private static List<(string Code, string? TeamName, string? Value)> Issues(ImportV1ProfileResponse body) =>
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
