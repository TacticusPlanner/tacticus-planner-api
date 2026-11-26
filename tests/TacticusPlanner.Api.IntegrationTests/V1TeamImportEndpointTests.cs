using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Teams;
using TacticusPlanner.Api.Features.V1Import;
using TacticusPlanner.Domain.Teams;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary><c>v1-team-import</c>: the <c>teams</c> part of <c>POST /account/v1-import</c>: reading <c>teams2</c>,
/// unit, mode, campaign and flex mapping, shortening, rule issues, re-runs by import key, the limit and outcomes. V1
/// blobs are written as JSON and read through the same reader the live client uses.</summary>
public sealed class V1TeamImportEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string subject = string.Empty;

    private Task<HttpClient> ClientAsync()
    {
        subject = $"v1-team-import-{Guid.NewGuid()}";
        return GoalsTestHelpers.CreateProvisionedClientAsync(factory, subject);
    }

    private string Configure(string dataJson)
    {
        var data = JsonSerializer.Deserialize<V1UserData>(dataJson, TacticusV1Client.WebJsonOptions);
        return factory.V1Client.ConfigureProfile(new TacticusV1Profile(null, null)
        {
            Teams = TacticusV1Client.ReadTeams(data?.Teams2, data?.SurvivalTeams),
        });
    }

    private static string Teams(params string[] entries) => $$"""{"teams2": [{{string.Join(",", entries)}}]}""";

    private static string Team(string name, string chars = """["ultraCalgar", "ultraTitus", "ultraApothecary"]""", string extra = """, "horde": true""") =>
        $$"""{"name": {{JsonSerializer.Serialize(name)}}, "chars": {{chars}}{{extra}}}""";

    private static async Task<ImportV1ProfileResponse> ImportAsync(
        HttpClient client, string username, bool teams = true, bool goals = false)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/account/v1-import",
            new ImportV1ProfileRequest(
                username, FakeTacticusV1Client.ValidPassword,
                new ImportV1Selection(false, false, false, goals, !teams && !goals, false, Teams: teams)),
            Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(Ct))!;
    }

    private static async Task<IReadOnlyList<TeamResponse>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ListTeamsResponse>("/api/v1/teams", Ct))!.Teams;

    private static IEnumerable<(string Code, string? Value)> Issues(V1TeamOutcome outcome) =>
        outcome.Issues.Select(issue => (issue.Code, issue.Value));

    [Fact]
    public async Task NotSelectedLeavesTeamsAlone()
    {
        var client = await ClientAsync();

        var body = await ImportAsync(client, Configure(Teams(Team("Horde core"))), teams: false);

        Assert.Equal((ImportPartStatus.Skipped, "not_selected"), (body.Teams.Status, body.Teams.Code));
        Assert.Empty(body.TeamOutcomes);
        Assert.Empty(await ListAsync(client));
        Assert.Equal((ImportPartStatus.Skipped, "not_selected"), (body.RosterSnapshots.Status, body.RosterSnapshots.Code));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"teams2": []}""")]
    public async Task NoV1TeamsIsSkipped(string json)
    {
        var client = await ClientAsync();

        var body = await ImportAsync(client, Configure(json));

        Assert.Equal((ImportPartStatus.Skipped, "missing_teams"), (body.Teams.Status, body.Teams.Code));
    }

    [Fact]
    public async Task UnreadableTeamsFailOnlyThisPart()
    {
        var client = await ClientAsync();
        var username = Configure("""{"teams2": {"name": "not a list"}}""");

        var body = await ImportAsync(client, username, teams: true, goals: true);

        Assert.Equal((ImportPartStatus.Failed, "invalid_teams"), (body.Teams.Status, body.Teams.Code));
        // The goals part ran on its own terms: this account has no player data, so it reports that, not the teams.
        Assert.Equal("player_data_required", body.Goals.Code);
        Assert.Empty(await ListAsync(client));
    }

    [Fact]
    public async Task AV1TeamBecomesAV2Team()
    {
        var client = await ClientAsync();
        var username = Configure(Teams(Team(
            "GW Ultras",
            """["ultraTigurius", "notAUnit", "ultraTitus", "ultraApothecary"]""",
            """, "flexIndex": 2, "mows": ["ultraDreadnought"], "warOffense": true, "raid": true, "notes": "  Titus flex  " """)));

        var body = await ImportAsync(client, username);

        var outcome = Assert.Single(body.TeamOutcomes);
        Assert.Equal(("GW Ultras", ImportPartStatus.Imported, null), (outcome.Name, outcome.Status, outcome.Code));
        Assert.Equal([("unit_not_recognised", (string?)"notAUnit")], Issues(outcome));
        Assert.Equal(ImportPartStatus.Imported, body.Teams.Status);
        var team = Assert.Single(await ListAsync(client));
        Assert.Equal(outcome.TeamId, team.Id);
        Assert.Equal(["ultraTigurius", "ultraTitus", "ultraApothecary"], team.CharacterIds);
        Assert.Equal(1, team.FlexStartIndex);
        Assert.Equal([TeamMode.WarOffense, TeamMode.GuildRaid], team.Modes);
        Assert.Equal(["ultraDreadnought"], team.MowIds);
        Assert.Equal("Titus flex", team.Notes);
        Assert.Empty(team.Issues);
    }

    [Fact]
    public async Task AnAllFlexV1TeamBecomesAllCoreAndIsReported()
    {
        var client = await ClientAsync();

        var body = await ImportAsync(client, Configure(Teams(Team(
            "All flex", """["ultraTigurius", "ultraTitus", "ultraApothecary"]""", """, "flexIndex": 0, "horde": true """))));

        Assert.Equal([("flex_dropped", (string?)null)], Issues(Assert.Single(body.TeamOutcomes)));
        var team = Assert.Single(await ListAsync(client));
        Assert.Equal(["ultraTigurius", "ultraTitus", "ultraApothecary"], team.CharacterIds);
        Assert.Null(team.FlexStartIndex);
    }

    [Fact]
    public async Task AFlexSectionAfterOnlyUnknownCoreUnitsIsReported()
    {
        var client = await ClientAsync();

        var body = await ImportAsync(client, Configure(Teams(Team(
            "Lost core", """["notAUnit", "ultraTitus", "ultraApothecary", "ultraCalgar"]""", """, "flexIndex": 1, "horde": true """))));

        Assert.Equal(
            [("unit_not_recognised", (string?)"notAUnit"), ("flex_dropped", null)],
            Issues(Assert.Single(body.TeamOutcomes)));
        Assert.Null(Assert.Single(await ListAsync(client)).FlexStartIndex);
    }

    [Fact]
    public async Task RepeatedUnitsAreKeptOnceAndReported()
    {
        var client = await ClientAsync();

        var body = await ImportAsync(client, Configure(Teams(Team(
            "Repeats",
            """["ultraTigurius", "ultraTigurius", "ultraTitus", "ultraApothecary"]""",
            """, "flexIndex": 3, "mows": ["ultraDreadnought", "ultraDreadnought"], "horde": true """))));

        Assert.Equal(
            [("duplicate_unit", (string?)"ultraTigurius"), ("duplicate_unit", "ultraDreadnought")],
            Issues(Assert.Single(body.TeamOutcomes)));
        var team = Assert.Single(await ListAsync(client));
        Assert.Equal(["ultraTigurius", "ultraTitus", "ultraApothecary"], team.CharacterIds);
        // V1's flex section began at its 4th entry; with the repeat removed it begins at the 3rd kept character.
        Assert.Equal(2, team.FlexStartIndex);
        Assert.Equal(["ultraDreadnought"], team.MowIds);
    }

    [Theory]
    [InlineData("indomitus", "campaign1")]
    [InlineData("indomitusMirror", "mirror1")]
    [InlineData("fallOfCadia", "campaign2")]
    [InlineData("fallOfCadiaMirror", "mirror2")]
    [InlineData("octarius", "campaign3")]
    [InlineData("octariusMirror", "mirror3")]
    [InlineData("saimHann", "campaign4")]
    [InlineData("saimHannMirror", "mirror4")]
    [InlineData("adMech", "eventCampaign1")]
    [InlineData("tyranids", "eventCampaign2")]
    [InlineData("tau", "eventCampaign3")]
    [InlineData("deathGuard", "eventCampaign4")]
    [InlineData("sisters", "eventCampaign5")]
    [InlineData("darkAngels", "eventCampaign6")]
    [InlineData("bloodAngels", "eventCampaign7")]
    public async Task CampaignStorylinesMapOntoCampaignGroups(string storyline, string groupId)
    {
        var client = await ClientAsync();

        await ImportAsync(client, Configure(Teams(Team(
            "Campaign team", extra: $$""", "campaign": true, "campaignStoryline": "{{storyline}}" """))));

        var team = Assert.Single(await ListAsync(client));
        Assert.Equal([TeamMode.Campaign], team.Modes);
        Assert.Equal(groupId, team.CampaignGroupId);
    }

    [Fact]
    public async Task AnUnknownStorylineIsReportedAndLeavesNoCampaign()
    {
        var client = await ClientAsync();

        var body = await ImportAsync(client, Configure(Teams(Team(
            "Campaign team", extra: """, "campaign": true, "campaignStoryline": "eldar" """))));

        var outcome = Assert.Single(body.TeamOutcomes);
        Assert.Contains(("campaign_not_recognised", (string?)"eldar"), Issues(outcome));
        Assert.Contains(("campaignRequired", (string?)null), Issues(outcome));
        Assert.Null(Assert.Single(await ListAsync(client)).CampaignGroupId);
    }

    [Fact]
    public async Task LongNamesAndNotesAreShortenedAndReported()
    {
        var client = await ClientAsync();
        var name = new string('n', 45);

        var body = await ImportAsync(client, Configure(Teams(Team(
            name, extra: $$""", "horde": true, "notes": {{JsonSerializer.Serialize(new string('x', 320))}}"""))));

        var outcome = Assert.Single(body.TeamOutcomes);
        Assert.Equal([("name_shortened", (string?)null), ("notes_shortened", null)], Issues(outcome));
        var team = Assert.Single(await ListAsync(client));
        Assert.Equal((40, 300), (team.Name.Length, team.Notes!.Length));
    }

    [Fact]
    public async Task ShorteningNeverSplitsAnEmoji()
    {
        var client = await ClientAsync();
        // 39 ASCII characters then an emoji (two UTF-16 code units), so a 40-unit cut lands inside the pair.
        var name = new string('n', 39) + "\U0001F600 tail";
        var notes = new string('x', 299) + "\U0001F600 tail";

        var body = await ImportAsync(client, Configure(Teams(Team(
            name, extra: $$""", "horde": true, "notes": {{JsonSerializer.Serialize(notes)}}"""))));

        var outcome = Assert.Single(body.TeamOutcomes);
        Assert.Equal(ImportPartStatus.Imported, outcome.Status);
        Assert.Equal([("name_shortened", (string?)null), ("notes_shortened", null)], Issues(outcome));
        var team = Assert.Single(await ListAsync(client));
        Assert.Equal((new string('n', 39), new string('x', 299)), (team.Name, team.Notes));
    }

    [Fact]
    public async Task ATeamThatBreaksARuleIsImportedAsV1HadIt()
    {
        var client = await ClientAsync();

        var body = await ImportAsync(client, Configure(Teams(
            Team("Six core", """["ultraCalgar", "ultraTitus", "ultraTigurius", "ultraEliminatorSgt", "ultraInceptorSgt", "ultraApothecary"]""",
                """, "warOffense": true"""),
            Team("Rukka sisters", """["ultraCalgar", "ultraTitus", "adeptCanoness"]""", """, "incursion": true, "incursionMows": ["orksRukkatrukk"]"""))));

        Assert.Equal([("tooManyCoreCharacters", (string?)null)], Issues(body.TeamOutcomes[0]));
        Assert.Equal([("factionNotAllowed", (string?)null)], Issues(body.TeamOutcomes[1]));
        var teams = await ListAsync(client);
        Assert.Equal(6, teams[0].CharacterIds.Count);
        Assert.Equal([TeamRuleCode.TooManyCoreCharacters], teams[0].Issues);
        Assert.Equal([TeamRuleCode.FactionNotAllowed], teams[1].Issues);
    }

    [Fact]
    public async Task AV1TeamOfTwoCharactersIsImportedWithItsIssue()
    {
        var client = await ClientAsync();

        var body = await ImportAsync(client, Configure(Teams(Team("Pair", """["ultraCalgar", "ultraTitus"]"""))));

        Assert.Equal([("tooFewCharacters", (string?)null)], Issues(Assert.Single(body.TeamOutcomes)));
        Assert.Equal([TeamRuleCode.TooFewCharacters], Assert.Single(await ListAsync(client)).Issues);
    }

    [Fact]
    public async Task AV1TeamWithSeveralMowsKeepsTheFirstAsCoreAndTheRestAsFlex()
    {
        var client = await ClientAsync();

        var body = await ImportAsync(client, Configure(Teams(
            Team("Two MoWs", extra: """, "warOffense": true, "mows": ["ultraDreadnought", "tauBroadside"]"""))));

        Assert.Empty(Issues(body.TeamOutcomes[0]));
        var team = Assert.Single(await ListAsync(client));
        Assert.Equal(["ultraDreadnought", "tauBroadside"], team.MowIds);
        Assert.Equal(1, team.MowFlexStartIndex);
        Assert.Null(team.RarityCap);
        Assert.Empty(team.Issues);
    }

    [Fact]
    public async Task EmptyAndUnreadableEntriesAreNotImported()
    {
        var client = await ClientAsync();

        var body = await ImportAsync(client, Configure(Teams(
            Team("Ghosts", """["notAUnit"]"""),
            """{"chars": ["ultraCalgar", "ultraTitus", "ultraApothecary"], "horde": true}""",
            Team("First valid"),
            Team("Second valid"))));

        Assert.Equal(
            [("Ghosts", ImportPartStatus.Skipped, "no_units"), (null, ImportPartStatus.Failed, "invalid_team"),
             ("First valid", ImportPartStatus.Imported, null), ("Second valid", ImportPartStatus.Imported, null)],
            body.TeamOutcomes.Select(outcome => (outcome.Name, outcome.Status, outcome.Code)));
        Assert.Equal(ImportPartStatus.Imported, body.Teams.Status);
        Assert.Equal(2, (await ListAsync(client)).Count);
    }

    [Fact]
    public async Task TeamsAreImportedInV1PriorityOrderAfterExistingTeams()
    {
        var client = await ClientAsync();
        (await client.PostAsJsonAsync("/api/v1/teams",
            new CreateTeamRequest("Made in V2", [TeamMode.Horde], ["ultraCalgar", "ultraTitus", "ultraApothecary"], null, [], null, null, [], null, null), Ct))
            .EnsureSuccessStatusCode();

        await ImportAsync(client, Configure(Teams(
            Team("Third", extra: """, "horde": true, "priority": 3"""),
            Team("First", extra: """, "horde": true, "priority": 1"""),
            Team("Second", extra: """, "horde": true, "priority": 2"""))));

        Assert.Equal(["Made in V2", "First", "Second", "Third"], (await ListAsync(client)).Select(team => team.Name));
    }

    [Fact]
    public async Task ARerunChangesNothing()
    {
        var client = await ClientAsync();
        var username = Configure(Teams(
            Team("Team one"), Team("Team two"), Team("Team three"), Team("Team four"), Team("Team five")));
        await ImportAsync(client, username);
        var before = await ListAsync(client);

        var body = await ImportAsync(client, username);

        Assert.All(body.TeamOutcomes, outcome => Assert.Equal((ImportPartStatus.Skipped, "already_imported"), (outcome.Status, outcome.Code)));
        Assert.Equal((ImportPartStatus.Skipped, "teams_skipped"), (body.Teams.Status, body.Teams.Code));
        Assert.Equivalent(before, await ListAsync(client), strict: true);
    }

    [Fact]
    public async Task ARerunAfterARenameCreatesNoCopy()
    {
        var client = await ClientAsync();
        var username = Configure(Teams(Team("GW Ultras")));
        await ImportAsync(client, username);
        var team = Assert.Single(await ListAsync(client));
        (await client.PutAsJsonAsync($"/api/v1/teams/{team.Id}", new UpdateTeamRequest(
            "Ultra D1", team.Modes, team.CharacterIds, null, [], null, null, [], null, null, team.Revision), Ct)).EnsureSuccessStatusCode();

        var body = await ImportAsync(client, username);

        Assert.Equal(("GW Ultras", ImportPartStatus.Skipped, "already_imported"),
            (body.TeamOutcomes[0].Name, body.TeamOutcomes[0].Status, body.TeamOutcomes[0].Code));
        Assert.Equal("Ultra D1", Assert.Single(await ListAsync(client)).Name);
    }

    [Fact]
    public async Task ARerunAfterADeleteImportsAgain()
    {
        var client = await ClientAsync();
        var username = Configure(Teams(Team("GW Ultras")));
        await ImportAsync(client, username);
        var team = Assert.Single(await ListAsync(client));
        (await client.DeleteAsync($"/api/v1/teams/{team.Id}", Ct)).EnsureSuccessStatusCode();

        var body = await ImportAsync(client, username);

        Assert.Equal(ImportPartStatus.Imported, Assert.Single(body.TeamOutcomes).Status);
        Assert.Equal("GW Ultras", Assert.Single(await ListAsync(client)).Name);
    }

    [Fact]
    public async Task ANameTakenByATeamMadeInV2IsADuplicate()
    {
        var client = await ClientAsync();
        (await client.PostAsJsonAsync("/api/v1/teams",
            new CreateTeamRequest("Horde", [TeamMode.Horde], ["ultraCalgar", "ultraTitus", "ultraApothecary"], null, [], null, null, [], null, null), Ct))
            .EnsureSuccessStatusCode();

        var body = await ImportAsync(client, Configure(Teams(Team("horde"))));

        Assert.Equal((ImportPartStatus.Skipped, "duplicate_name"), (body.TeamOutcomes[0].Status, body.TeamOutcomes[0].Code));
        Assert.Single(await ListAsync(client));
    }

    [Fact]
    public async Task KeysCollidingInsideOneImportAreImportedOnce()
    {
        var client = await ClientAsync();

        var body = await ImportAsync(client, Configure(Teams(Team("Twins"), Team("  twins ", """["ultraTitus", "ultraTigurius", "ultraApothecary"]"""))));

        Assert.Equal(
            [(ImportPartStatus.Imported, null), (ImportPartStatus.Skipped, "already_imported")],
            body.TeamOutcomes.Select(outcome => (outcome.Status, outcome.Code)));
        Assert.Equal(["ultraCalgar", "ultraTitus", "ultraApothecary"], Assert.Single(await ListAsync(client)).CharacterIds);
    }

    [Fact]
    public async Task OnlyTheRoomUnderTheLimitIsUsed()
    {
        var client = await ClientAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
            var profileId = (await db.Profiles.IgnoreQueryFilters().FirstAsync(entity => entity.Subject == subject, Ct)).Id;
            db.Teams.AddRange(Enumerable.Range(1, TeamValidation.DefaultLimit - 2).Select(i => Domain.Teams.Team.Create(
                profileId, new TeamDraft($"Seeded {i}", [TeamMode.Horde], ["ultraCalgar"], null, [], null, [], null), i)));
            await db.SaveChangesAsync(Ct);
        }

        var body = await ImportAsync(client, Configure(Teams(
            Team("Import four", extra: """, "horde": true, "priority": 4"""),
            Team("Import one", extra: """, "horde": true, "priority": 1"""),
            Team("Import three", extra: """, "horde": true, "priority": 3"""),
            Team("Import two", extra: """, "horde": true, "priority": 2"""))));

        Assert.Equal(
            [("Import one", ImportPartStatus.Imported, null), ("Import two", ImportPartStatus.Imported, null),
             ("Import three", ImportPartStatus.Skipped, "team_limit_reached"), ("Import four", ImportPartStatus.Skipped, "team_limit_reached")],
            body.TeamOutcomes.Select(outcome => (outcome.Name, outcome.Status, outcome.Code)));
        Assert.Equal(TeamValidation.DefaultLimit, (await ListAsync(client)).Count);
    }

    [Fact]
    public async Task TheImportReadsTheConfiguredLimit()
    {
        using var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("Teams:Limit", "1"));
        var client = limited.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, $"v1-team-import-{Guid.NewGuid()}");
        await client.GetAsync("/api/v1/account", Ct);

        var body = await ImportAsync(client, Configure(Teams(Team("Team one"), Team("Team two"))));

        Assert.Equal(
            [(ImportPartStatus.Imported, null), (ImportPartStatus.Skipped, "team_limit_reached")],
            body.TeamOutcomes.Select(outcome => (outcome.Status, outcome.Code)));
    }

    [Fact]
    public async Task V1SurvivalTeamsBecomeTeamsTiedToTheirEvent()
    {
        var client = await ClientAsync();
        var username = Configure("""
            {"survivalTeams": {
                "season_december_2026_event": ["admecManipulus", "ultraCalgar", "ultraTitus", "ultraApothecary"],
                "season_crescendo_01_event": ["ultraCalgar"],
                "season_may_2026_event": []
            }}
            """);

        var body = await ImportAsync(client, username);

        Assert.Equal(
            [(ImportPartStatus.Imported, (string?)null), (ImportPartStatus.Skipped, "survival_event_not_recognised")],
            body.TeamOutcomes.Select(outcome => (outcome.Status, outcome.Code)));
        var team = Assert.Single(await ListAsync(client));
        Assert.Equal("Survival · December 2026", team.Name);
        Assert.Equal("december-2026", team.SurvivalEventKey);
        Assert.Equal([TeamMode.Horde], team.Modes);
        Assert.Equal(["ultraCalgar", "ultraTitus", "ultraApothecary"], team.CharacterIds);
        Assert.Empty(team.Issues);
    }

    [Fact]
    public async Task SurvivalTeamsAreNotCutByTheTeams2Cap()
    {
        using var roomy = factory.WithWebHostBuilder(builder => builder.UseSetting("Teams:Limit", "200"));
        var client = roomy.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, $"v1-team-import-{Guid.NewGuid()}");
        await client.GetAsync("/api/v1/account", Ct);
        var teams2 = string.Join(",", Enumerable.Range(1, V1TeamImportService.MaxTeams + 1).Select(i => Team($"Team {i}")));
        const string survival = """{"season_december_2026_event": ["ultraCalgar", "ultraTitus", "ultraApothecary"]}""";
        var username = Configure($$"""{"teams2": [{{teams2}}], "survivalTeams": {{survival}}}""");

        var body = await ImportAsync(client, username);

        Assert.Equal(V1TeamImportService.MaxTeams + 1, body.TeamOutcomes.Count);
        Assert.All(body.TeamOutcomes, outcome => Assert.Equal(ImportPartStatus.Imported, outcome.Status));
        Assert.Contains(await ListAsync(client), team => team.SurvivalEventKey == "december-2026");
    }
}
