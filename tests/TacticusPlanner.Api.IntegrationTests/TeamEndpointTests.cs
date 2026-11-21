using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Teams;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Teams;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary><c>personal-teams</c>: list order and isolation, create with limits and rules, update with revisions,
/// delete closing the gap, whole-list reorder, and issues derived on read.</summary>
public sealed class TeamEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string Url = "/api/v1/teams";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CreateTeamRequest Create(
        string name,
        TeamMode[]? modes = null,
        string[]? characters = null,
        int? flex = null,
        string[]? mows = null,
        string? notes = null,
        int? mowFlex = null,
        TeamRarityCap? cap = null) =>
        new(name, modes ?? [TeamMode.Horde], characters ?? ["ultraCalgar", "ultraTitus", "ultraApothecary"], flex, mows ?? [], mowFlex, null, [], notes,
            cap);

    private static UpdateTeamRequest Update(TeamResponse team, string? name = null, string[]? characters = null) =>
        new(name ?? team.Name, team.Modes, characters ?? team.CharacterIds, team.FlexStartIndex, team.MowIds,
            team.MowFlexStartIndex, team.CampaignGroupId, team.IncursionMowIds, team.Notes, team.RarityCap, team.Revision);

    private async Task<(HttpClient Client, string Subject)> ClientAsync()
    {
        var subject = $"teams-{Guid.NewGuid()}";
        return (await GoalsTestHelpers.CreateProvisionedClientAsync(factory, subject), subject);
    }

    private static async Task<TeamResponse> CreateAsync(HttpClient client, CreateTeamRequest request)
    {
        var response = await client.PostAsJsonAsync(Url, request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TeamResponse>(Ct))!;
    }

    private static async Task<IReadOnlyList<TeamResponse>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ListTeamsResponse>(Url, Ct))!.Teams;

    private static async Task<JsonArray> RulesAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadProblemAsync();
        Assert.Equal(TeamsService.RulesBroken, problem["issueCode"]!.GetValue<string>());
        return problem["rules"]!.AsArray();
    }

    /// <summary>Writes a team straight into the profile, bypassing the rules: an imported or outdated team.</summary>
    private async Task SeedAsync(string subject, params TeamDraft[] drafts)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var profileId = (await db.Profiles.IgnoreQueryFilters().FirstAsync(entity => entity.Subject == subject, Ct)).Id;
        var existing = await db.Teams.IgnoreQueryFilters().CountAsync(team => team.ProfileId == profileId, Ct);
        db.Teams.AddRange(drafts.Select((draft, index) => Team.Create(profileId, draft, existing + index + 1)));
        await db.SaveChangesAsync(Ct);
    }

    private static TeamDraft Seeded(string name, string[]? characters = null, TeamMode mode = TeamMode.Horde) =>
        new(name, [mode], characters ?? ["ultraCalgar", "ultraTitus", "ultraApothecary"], null, [], null, [], null);

    [Fact]
    public async Task ACreatedTeamIsReturnedAsListed()
    {
        var (client, _) = await ClientAsync();

        var created = await CreateAsync(client, Create(
            "  Ultra wall  ", [TeamMode.WarDefense, TeamMode.Horde], ["ultraCalgar", "ultraTitus", "bloodDante"], flex: 2,
            mows: ["ultraDreadnought"], notes: "   "));

        Assert.Equal("Ultra wall", created.Name);
        Assert.Equal(1, created.Position);
        Assert.Equal(1, created.Revision);
        Assert.Equal([TeamMode.WarDefense, TeamMode.Horde], created.Modes);
        Assert.Equal(2, created.FlexStartIndex);
        Assert.Null(created.Notes);
        Assert.Empty(created.Issues);
        Assert.Equivalent(created, Assert.Single(await ListAsync(client)), strict: true);
    }

    [Fact]
    public async Task TeamsAreListedInOrderAndScopedToTheirProfile()
    {
        var (client, _) = await ClientAsync();
        var (other, _) = await ClientAsync();
        await CreateAsync(client, Create("First"));
        await CreateAsync(client, Create("Second"));
        var foreign = await CreateAsync(other, Create("Theirs"));

        Assert.Equal(["First", "Second"], (await ListAsync(client)).Select(team => team.Name));
        Assert.Equal([1, 2], (await ListAsync(client)).Select(team => team.Position));
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync($"{Url}/{foreign.Id}", Update(foreign, "Mine now"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"{Url}/{foreign.Id}", Ct)).StatusCode);
        Assert.Single(await ListAsync(other));
    }

    [Fact]
    public async Task AUnitNoLongerInTheCatalogIsReportedOnRead()
    {
        var (client, subject) = await ClientAsync();
        await SeedAsync(subject, Seeded("Retired unit", ["ultraCalgar", "ultraTitus", "notAUnitAnyMore"]));

        Assert.Equal([TeamRuleCode.UnknownUnit], Assert.Single(await ListAsync(client)).Issues);
    }

    [Fact]
    public async Task ABrokenRuleIsRefusedNamingEachRule()
    {
        var (client, _) = await ClientAsync();

        var rules = await RulesAsync(await client.PostAsJsonAsync(Url, Create(
            "ab", [TeamMode.WarOffense], ["ultraCalgar", "ultraTitus", "ultraApothecary", "ultraTigurius", "ultraEliminatorSgt", "ultraInceptorSgt"]), Ct));

        Assert.Equal(["nameLength", "tooManyCoreCharacters"], rules.Select(rule => rule!.GetValue<string>()));
        Assert.Empty(await ListAsync(client));
    }

    [Fact]
    public async Task ASavedRarityCapAndMowFlexAreReturned()
    {
        var (client, _) = await ClientAsync();

        var created = await CreateAsync(client, Create(
            "Capped war", [TeamMode.WarOffense], mows: ["ultraDreadnought", "tauBroadside"], mowFlex: 1, cap: TeamRarityCap.Epic));

        var team = Assert.Single(await ListAsync(client));
        Assert.Equal(created.Id, team.Id);
        Assert.Equal(TeamRarityCap.Epic, team.RarityCap);
        Assert.Equal(1, team.MowFlexStartIndex);
        Assert.Empty(team.Issues);
    }

    [Fact]
    public async Task TwoCoreMowsInWarAreRefused()
    {
        var (client, _) = await ClientAsync();

        var rules = await RulesAsync(await client.PostAsJsonAsync(Url, Create(
            "Two core", [TeamMode.WarDefense], mows: ["ultraDreadnought", "tauBroadside"]), Ct));

        Assert.Equal(["tooManyCoreMows"], rules.Select(rule => rule!.GetValue<string>()));
    }

    [Fact]
    public async Task AnUnknownRarityCapIsRefused()
    {
        var (client, _) = await ClientAsync();

        using var content = new StringContent(
            """{"name":"Shiny","modes":["horde"],"characterIds":["ultraCalgar","ultraTitus","ultraApothecary"],"mowIds":[],"incursionMowIds":[],"rarityCap":"Shiny"}""",
            System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync(Url, content, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadProblemAsync();
        Assert.Equal("validation", problem["issueCode"]?.GetValue<string>());
        Assert.Contains(problem["errors"]!.AsObject(), error => error.Key.EndsWith("rarityCap", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await ListAsync(client));
    }

    [Theory]
    [InlineData("modes")]
    [InlineData("characterIds")]
    [InlineData("mowIds")]
    [InlineData("incursionMowIds")]
    public async Task ANullLineupListIsAnInvalidShape(string field)
    {
        var (client, _) = await ClientAsync();
        var body = new System.Text.Json.Nodes.JsonObject
        {
            ["name"] = "Nulls",
            ["modes"] = new System.Text.Json.Nodes.JsonArray("horde"),
            ["characterIds"] = new System.Text.Json.Nodes.JsonArray("ultraCalgar", "ultraTitus", "ultraApothecary"),
            ["mowIds"] = new System.Text.Json.Nodes.JsonArray(),
            ["incursionMowIds"] = new System.Text.Json.Nodes.JsonArray(),
        };
        body[field] = null;

        using var content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync(Url, content, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation", await response.ReadIssueCodeAsync());
        Assert.Empty(await ListAsync(client));
    }

    [Fact]
    public async Task AMowFlexIndexPastTheMowsIsAnInvalidShape()
    {
        var (client, _) = await ClientAsync();

        var response = await client.PostAsJsonAsync(Url, Create("Past", mows: ["ultraDreadnought"], mowFlex: 1), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation", await response.ReadIssueCodeAsync());
    }

    [Fact]
    public async Task ADuplicateUnitIsAnInvalidShape()
    {
        var (client, _) = await ClientAsync();

        var response = await client.PostAsJsonAsync(Url, Create("Twice", characters: ["ultraCalgar", "ultraCalgar"]), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation", await response.ReadIssueCodeAsync());
    }

    [Fact]
    public async Task SameNameInAnotherCaseIsTaken()
    {
        var (client, _) = await ClientAsync();
        await CreateAsync(client, Create("Mortarion core"));

        var response = await client.PostAsJsonAsync(Url, Create("mortarion CORE"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TeamsService.NameTaken, await response.ReadIssueCodeAsync());
    }

    [Fact]
    public async Task TheTwentyFirstTeamIsRefused()
    {
        var (client, subject) = await ClientAsync();
        await SeedAsync(subject, [.. Enumerable.Range(1, 20).Select(i => Seeded($"Seeded {i}"))]);

        var response = await client.PostAsJsonAsync(Url, Create("One too many"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TeamsService.LimitReached, await response.ReadIssueCodeAsync());
        Assert.Equal(20, (await ListAsync(client)).Count);
    }

    [Fact]
    public async Task ConcurrentCreatesCannotPassTheLimit()
    {
        var (client, subject) = await ClientAsync();
        await SeedAsync(subject, [.. Enumerable.Range(1, 18).Select(i => Seeded($"Seeded {i}"))]);

        var responses = await Task.WhenAll(Enumerable.Range(1, 6)
            .Select(i => client.PostAsJsonAsync(Url, Create($"Racer {i}"), Ct)));

        Assert.Equal(2, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        var teams = await ListAsync(client);
        Assert.Equal(20, teams.Count);
        Assert.Equal(Enumerable.Range(1, 20), teams.Select(team => team.Position));
    }

    [Fact]
    public async Task TheLimitIsConfigured()
    {
        using var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("Teams:Limit", "2"));
        var client = limited.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, $"teams-{Guid.NewGuid()}");
        await client.GetAsync("/api/v1/account", Ct);
        await CreateAsync(client, Create("First"));
        await CreateAsync(client, Create("Second"));

        var response = await client.PostAsJsonAsync(Url, Create("Third"), Ct);

        Assert.Equal(TeamsService.LimitReached, await response.ReadIssueCodeAsync());
    }

    [Fact]
    public async Task AStaleUpdateIsRefusedWithTheCurrentTeam()
    {
        var (client, _) = await ClientAsync();
        var created = await CreateAsync(client, Create("Original"));
        var renamed = (await (await client.PutAsJsonAsync($"{Url}/{created.Id}", Update(created, "Renamed"), Ct))
            .Content.ReadFromJsonAsync<TeamResponse>(Ct))!;

        var response = await client.PutAsJsonAsync($"{Url}/{created.Id}", Update(created, "Lost edit"), Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(TeamsService.RevisionStale, await response.ReadIssueCodeAsync());
        var current = await response.ReadCurrentAsync<TeamResponse>();
        Assert.Equal(("Renamed", 2L), (current.Name, current.Revision));
        Assert.Equal(renamed.Revision, Assert.Single(await ListAsync(client)).Revision);
    }

    [Fact]
    public async Task ARenameKeepsTheId()
    {
        var (client, _) = await ClientAsync();
        var created = await CreateAsync(client, Create("Before"));

        var response = await client.PutAsJsonAsync($"{Url}/{created.Id}", Update(created, "After"), Ct);

        var renamed = (await response.Content.ReadFromJsonAsync<TeamResponse>(Ct))!;
        Assert.Equal((created.Id, "After", 2L), (renamed.Id, renamed.Name, renamed.Revision));
    }

    [Fact]
    public async Task ARenameCannotSkipAnIssue()
    {
        var (client, subject) = await ClientAsync();
        await SeedAsync(subject, Seeded(
            "Too big", ["ultraCalgar", "ultraTitus", "ultraApothecary", "ultraTigurius", "ultraEliminatorSgt", "ultraInceptorSgt"],
            TeamMode.WarOffense));
        var team = Assert.Single(await ListAsync(client));
        Assert.Equal([TeamRuleCode.TooManyCoreCharacters], team.Issues);

        var rules = await RulesAsync(await client.PutAsJsonAsync($"{Url}/{team.Id}", Update(team, "Renamed"), Ct));

        Assert.Equal("tooManyCoreCharacters", Assert.Single(rules)!.GetValue<string>());
        Assert.Equal("Too big", Assert.Single(await ListAsync(client)).Name);
    }

    [Fact]
    public async Task RenamingToAnotherTeamsNameIsTaken()
    {
        var (client, _) = await ClientAsync();
        await CreateAsync(client, Create("Taken"));
        var second = await CreateAsync(client, Create("Free"));

        var sameCase = await client.PutAsJsonAsync($"{Url}/{second.Id}", Update(second, "taken"), Ct);
        var ownName = await client.PutAsJsonAsync($"{Url}/{second.Id}", Update(second, "FREE"), Ct);

        Assert.Equal(TeamsService.NameTaken, await sameCase.ReadIssueCodeAsync());
        Assert.Equal(HttpStatusCode.OK, ownName.StatusCode);
    }

    [Fact]
    public async Task DeleteClosesTheGap()
    {
        var (client, _) = await ClientAsync();
        var first = await CreateAsync(client, Create("First"));
        var second = await CreateAsync(client, Create("Second"));
        var third = await CreateAsync(client, Create("Third"));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Url}/{second.Id}", Ct)).StatusCode);

        var teams = await ListAsync(client);
        Assert.Equal([(first.Id, 1), (third.Id, 2)], teams.Select(team => (team.Id, team.Position)));
        Assert.Equal([1L, 1L], teams.Select(team => team.Revision));
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"{Url}/{second.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task AnUpdateAfterADeleteReturnsTheStoredPosition()
    {
        var (client, _) = await ClientAsync();
        var first = await CreateAsync(client, Create("First"));
        var second = await CreateAsync(client, Create("Second"));
        var third = await CreateAsync(client, Create("Third"));

        // A delete moves the later teams up without changing their revision, so an edit made before it still saves.
        await client.DeleteAsync($"{Url}/{first.Id}", Ct);
        await client.DeleteAsync($"{Url}/{second.Id}", Ct);
        var response = await client.PutAsJsonAsync($"{Url}/{third.Id}", Update(third, "Renamed"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<TeamResponse>(Ct))!;
        Assert.Equal(("Renamed", 1), (updated.Name, updated.Position));
        Assert.Equal(1, Assert.Single(await ListAsync(client)).Position);
    }

    [Fact]
    public async Task AReorderChangesNoOtherField()
    {
        var (client, _) = await ClientAsync();
        var a = await CreateAsync(client, Create("Team A"));
        var b = await CreateAsync(client, Create("Team B"));
        var c = await CreateAsync(client, Create("Team C"));

        var response = await client.PutAsJsonAsync($"{Url}/order", new UpdateTeamOrderRequest([c.Id, b.Id, a.Id]), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reordered = (await response.Content.ReadFromJsonAsync<ListTeamsResponse>(Ct))!.Teams;
        Assert.Equal([c.Id, b.Id, a.Id], reordered.Select(team => team.Id));
        Assert.Equal([1, 2, 3], reordered.Select(team => team.Position));
        Assert.Equivalent(a with { Position = 3 }, reordered[2], strict: true);
        Assert.Equivalent(reordered, await ListAsync(client), strict: true);
    }

    [Fact]
    public async Task AReorderMissingATeamIsStale()
    {
        var (client, _) = await ClientAsync();
        var a = await CreateAsync(client, Create("Team A"));
        var b = await CreateAsync(client, Create("Team B"));
        await CreateAsync(client, Create("Added elsewhere"));

        var response = await client.PutAsJsonAsync($"{Url}/order", new UpdateTeamOrderRequest([b.Id, a.Id]), Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(TeamsService.OrderStale, await response.ReadIssueCodeAsync());
        Assert.Equal(3, (await response.ReadCurrentAsync<ListTeamsResponse>()).Teams.Count);
        Assert.Equal(["Team A", "Team B", "Added elsewhere"], (await ListAsync(client)).Select(team => team.Name));
    }

    [Fact]
    public async Task AReorderWithADuplicateIsStale()
    {
        var (client, _) = await ClientAsync();
        var a = await CreateAsync(client, Create("Team A"));
        await CreateAsync(client, Create("Team B"));

        var response = await client.PutAsJsonAsync($"{Url}/order", new UpdateTeamOrderRequest([a.Id, a.Id]), Ct);

        Assert.Equal(TeamsService.OrderStale, await response.ReadIssueCodeAsync());
    }

    [Fact]
    public async Task TeamsAreDeletedWithTheProfile()
    {
        var (client, subject) = await ClientAsync();
        await CreateAsync(client, Create("Doomed"));
        ProfileId profileId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
            profileId = (await db.Profiles.IgnoreQueryFilters().FirstAsync(entity => entity.Subject == subject, Ct)).Id;
        }

        (await client.DeleteAsync("/api/v1/account", Ct)).EnsureSuccessStatusCode();

        using var verify = factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PlannerDbContext>();
        Assert.False(await verifyDb.Teams.IgnoreQueryFilters().AnyAsync(team => team.ProfileId == profileId, Ct));
    }
}
