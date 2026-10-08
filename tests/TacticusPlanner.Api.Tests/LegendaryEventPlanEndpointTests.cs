using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.LegendaryEventPlans;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Tests;

public sealed class LegendaryEventPlanEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string EventId = "astarLysander";
    private const string PlanUrl = $"/api/v1/me/legendary-event-plans/{EventId}";

    private readonly GameCatalogLreView lre = factory.Services.GetRequiredService<IGameCatalogProvider>()
        .Current.LreViews.Single(view => view.Id == EventId);

    private IReadOnlyList<string> AlphaUnits => lre.Alpha.AvailableUnitIds;

    private IReadOnlyList<string> BetaUnits => lre.Beta.AvailableUnitIds;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MissingPlanReadsAsEmptyRevisionZeroPlan()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var plan = await ReadPlanAsync(await client.GetAsync(PlanUrl, Ct));

        Assert.Equal(EventId, plan.EventId);
        Assert.Equal(0, plan.Revision);
        Assert.Null(plan.Notes);
        Assert.False(plan.ShowPaidOptions);
        Assert.Empty(plan.Teams);
        Assert.Equal(GameCatalogRelease.Version, plan.CatalogVersion);

        // Reading did not create a row: a write at revision 1 is stale against the empty plan.
        var stale = await client.PutAsJsonAsync(PlanUrl, new UpdateLegendaryEventPlanRequest(1, null, false), Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task UnknownEventIs404ForReadsAndWrites()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        const string unknown = "/api/v1/me/legendary-event-plans/notAnEvent";

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(unknown, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync(unknown, new UpdateLegendaryEventPlanRequest(0, null, false), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync($"{unknown}/teams", Team(0, "alpha", [AlphaUnits[0]]), Ct)).StatusCode);
    }

    [Fact]
    public async Task PlanLevelWriteCreatesThePlan()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var plan = await ReadPlanAsync(await client.PutAsJsonAsync(
            PlanUrl, new UpdateLegendaryEventPlanRequest(0, "Alpha first", true), Ct));

        Assert.Equal(1, plan.Revision);
        Assert.Equal("Alpha first", plan.Notes);
        Assert.True(plan.ShowPaidOptions);
        Assert.Equal(GameCatalogRelease.Version, plan.CatalogVersion);
        Assert.Equivalent(plan, await ReadPlanAsync(await client.GetAsync(PlanUrl, Ct)));
    }

    [Fact]
    public async Task NotesOverTheLimitAreRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PutAsJsonAsync(
            PlanUrl, new UpdateLegendaryEventPlanRequest(0, new string('n', 2001), false), Ct);

        await AssertBadRequestAsync(response, "notes");
    }

    [Fact]
    public async Task CreateTeamLazilyCreatesThePlanAndServesTheTeam()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var objectives = lre.Alpha.UnitsRestrictions.Select(restriction => restriction.Index).Take(2).ToList();

        var plan = await ReadPlanAsync(await client.PostAsJsonAsync($"{PlanUrl}/teams", new CreateLegendaryEventTeamRequest(
            0, "alpha", "  Melee  ", [AlphaUnits[2], AlphaUnits[0], AlphaUnits[1]], AlphaUnits[3], objectives.AsEnumerable().Reverse().ToList(),
            1, 7, "manual"), Ct));

        Assert.Equal(1, plan.Revision);
        var team = Assert.Single(plan.Teams);
        Assert.NotEqual(Guid.Empty, team.Id);
        Assert.Equal("alpha", team.LaneId);
        Assert.Equal("Melee", team.Name);
        Assert.Equal(0, team.SortOrder);
        Assert.Equal([AlphaUnits[2], AlphaUnits[0], AlphaUnits[1]], team.MemberUnitIds);
        Assert.Equal(AlphaUnits[3], team.ReserveUnitId);
        Assert.Equal(objectives, team.ObjectiveIndexes);
        var depth = Assert.Single(team.RunDepths);
        Assert.Equal((1, 7, "manual"), (depth.Run, depth.ExpectedBattleClears, depth.ExpectedBattleClearsSource));
    }

    [Fact]
    public async Task TeamsAreAppendedPerLaneAndServedInLaneThenOrder()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var plan = await CreateAsync(client, Team(0, "gamma", [lre.Gamma.AvailableUnitIds[0]], "g0"));
        plan = await CreateAsync(client, Team(plan.Revision, "alpha", [AlphaUnits[0]], "a0"));
        plan = await CreateAsync(client, Team(plan.Revision, "beta", [BetaUnits[0]], "b0"));
        plan = await CreateAsync(client, Team(plan.Revision, "alpha", [AlphaUnits[1]], "a1"));

        Assert.Equal(4, plan.Revision);
        Assert.Equal(["a0", "a1", "b0", "g0"], plan.Teams.Select(team => team.Name));
        Assert.Equal([0, 1, 0, 0], plan.Teams.Select(team => team.SortOrder));
    }

    [Fact]
    public async Task UpdateReplacesFieldsAndKeepsLaneAndOrder()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await CreateAsync(client, Team(0, "alpha", [AlphaUnits[0]], "first"));
        plan = await CreateAsync(client, Team(plan.Revision, "alpha", [AlphaUnits[0], AlphaUnits[1], AlphaUnits[2]], "second", reserve: AlphaUnits[3], objectives: [0]));
        var second = plan.Teams[1];

        // Swap positions, move the reserve into the line-up and change objectives in one write.
        plan = await ReadPlanAsync(await UpdateAsync(client, second.Id, new UpdateLegendaryEventTeamRequest(
            plan.Revision, "renamed", [AlphaUnits[3], AlphaUnits[2], AlphaUnits[0]], AlphaUnits[1], [1], 1, 5, "manual")));

        Assert.Equal(3, plan.Revision);
        var updated = plan.Teams.Single(team => team.Id == second.Id);
        Assert.Equal(("alpha", 1, "renamed"), (updated.LaneId, updated.SortOrder, updated.Name));
        Assert.Equal([AlphaUnits[3], AlphaUnits[2], AlphaUnits[0]], updated.MemberUnitIds);
        Assert.Equal(AlphaUnits[1], updated.ReserveUnitId);
        Assert.Equal([1], updated.ObjectiveIndexes);
        Assert.Equal(5, Assert.Single(updated.RunDepths).ExpectedBattleClears);
    }

    [Fact]
    public async Task ADepthWriteTouchesOnlyItsRun()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await CreateAsync(client, Team(0, "alpha", [AlphaUnits[0]], "t", depth: 7));
        var team = plan.Teams[0];
        var run1RecordedAt = team.RunDepths[0].RecordedAt;

        plan = await ReadPlanAsync(await UpdateAsync(client, team.Id, Update(plan.Revision, team, run: 2, depth: 9)));
        var depths = plan.Teams[0].RunDepths;
        Assert.Equal([(1, 7), (2, 9)], depths.Select(depth => (depth.Run, depth.ExpectedBattleClears)));
        Assert.Equal(run1RecordedAt, depths[0].RecordedAt);

        plan = await ReadPlanAsync(await UpdateAsync(client, team.Id, Update(plan.Revision, team, run: 1, depth: 8)));
        Assert.Equal([(1, 8), (2, 9)], plan.Teams[0].RunDepths.Select(depth => (depth.Run, depth.ExpectedBattleClears)));
        Assert.True(plan.Teams[0].RunDepths[0].RecordedAt >= run1RecordedAt);

        plan = await ReadPlanAsync(await UpdateAsync(client, team.Id, Update(plan.Revision, team, run: 2, depth: null)));
        Assert.Equal([1], plan.Teams[0].RunDepths.Select(depth => depth.Run));
    }

    [Fact]
    public async Task UpdateWithLaneIdIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await CreateAsync(client, Team(0, "alpha", [AlphaUnits[0]], "t"));
        var team = plan.Teams[0];

        var response = await UpdateAsync(client, team.Id, Update(plan.Revision, team) with { LaneId = "beta" });

        await AssertBadRequestAsync(response, "laneId");
    }

    [Fact]
    public async Task UnknownTeamIs404()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await CreateAsync(client, Team(0, "alpha", [AlphaUnits[0]], "t"));
        var missing = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound,
            (await UpdateAsync(client, missing, Update(plan.Revision, plan.Teams[0]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.DeleteAsync($"{PlanUrl}/teams/{missing}?expectedRevision={plan.Revision}", Ct)).StatusCode);
    }

    [Fact]
    public async Task DeleteReDensifiesTheLane()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await CreateAsync(client, Team(0, "alpha", [AlphaUnits[0]], "A"));
        plan = await CreateAsync(client, Team(plan.Revision, "alpha", [AlphaUnits[1]], "B"));
        plan = await CreateAsync(client, Team(plan.Revision, "alpha", [AlphaUnits[2]], "C"));

        plan = await ReadPlanAsync(await client.DeleteAsync(
            $"{PlanUrl}/teams/{plan.Teams[1].Id}?expectedRevision={plan.Revision}", Ct));

        Assert.Equal(4, plan.Revision);
        Assert.Equal([("A", 0), ("C", 1)], plan.Teams.Select(team => (team.Name, team.SortOrder)));
    }

    [Fact]
    public async Task DeleteWithoutExpectedRevisionIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await CreateAsync(client, Team(0, "alpha", [AlphaUnits[0]], "A"));

        await AssertBadRequestAsync(await client.DeleteAsync($"{PlanUrl}/teams/{plan.Teams[0].Id}", Ct), "expectedRevision");
    }

    public static TheoryData<string> InvalidTeams => ["laneId", "memberUnitIds", "duplicateMember", "tooManyMembers",
        "reserveUnitId", "reserveIsMember", "objectiveIndexes", "duplicateObjective", "run", "expectedBattleClears",
        "missingSource", "sourceWithoutDepth", "unknownSource", "name", "longName"];

    [Theory]
    [MemberData(nameof(InvalidTeams))]
    public async Task InvalidTeamsAreRejectedNamingTheField(string scenario)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var notAllowed = BetaUnits.Except(AlphaUnits).First();
        var valid = Team(0, "alpha", [AlphaUnits[0], AlphaUnits[1]], "t", depth: 3);
        var (request, field) = scenario switch
        {
            "laneId" => (valid with { LaneId = "delta" }, "laneId"),
            "memberUnitIds" => (valid with { MemberUnitIds = [notAllowed] }, "memberUnitIds"),
            "duplicateMember" => (valid with { MemberUnitIds = [AlphaUnits[0], AlphaUnits[0]] }, "memberUnitIds"),
            "tooManyMembers" => (valid with { MemberUnitIds = AlphaUnits.Take(6).ToList() }, "memberUnitIds"),
            "reserveUnitId" => (valid with { ReserveUnitId = notAllowed }, "reserveUnitId"),
            "reserveIsMember" => (valid with { ReserveUnitId = AlphaUnits[0] }, "reserveUnitId"),
            "objectiveIndexes" => (valid with { ObjectiveIndexes = [lre.Alpha.UnitsRestrictions.Count] }, "objectiveIndexes"),
            "duplicateObjective" => (valid with { ObjectiveIndexes = [0, 0] }, "objectiveIndexes"),
            "run" => (valid with { Run = 4 }, "run"),
            "expectedBattleClears" => (valid with { ExpectedBattleClears = lre.Alpha.BattleIds.Count + 1 }, "expectedBattleClears"),
            "missingSource" => (valid with { ExpectedBattleClearsSource = null }, "expectedBattleClearsSource"),
            "sourceWithoutDepth" => (valid with { ExpectedBattleClears = null }, "expectedBattleClearsSource"),
            "unknownSource" => (valid with { ExpectedBattleClearsSource = "guess" }, "expectedBattleClearsSource"),
            "name" => (valid with { Name = "   " }, "name"),
            "longName" => (valid with { Name = new string('x', 61) }, "name"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        await AssertBadRequestAsync(await client.PostAsJsonAsync($"{PlanUrl}/teams", request, Ct), field);
        Assert.Equal(0, (await ReadPlanAsync(await client.GetAsync(PlanUrl, Ct))).Revision);
    }

    [Fact]
    public async Task ATeamMayCoverZeroObjectives()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var plan = await CreateAsync(client, Team(0, "alpha", [AlphaUnits[0]], "t"));

        Assert.Empty(plan.Teams[0].ObjectiveIndexes);
        Assert.Empty(plan.Teams[0].RunDepths);
    }

    [Fact]
    public async Task ReorderReplacesTheLaneOrderAndBumpsTheRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await ThreeAlphaTeamsAsync(client);
        var (a, b, c) = (plan.Teams[0].Id, plan.Teams[1].Id, plan.Teams[2].Id);

        plan = await ReadPlanAsync(await ReorderAsync(client, plan.Revision, [c, a, b]));

        Assert.Equal(4, plan.Revision);
        Assert.Equal([("C", 0), ("A", 1), ("B", 2)], plan.Teams.Select(team => (team.Name, team.SortOrder)));
    }

    [Fact]
    public async Task ReorderToTheCurrentOrderKeepsTheRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await ThreeAlphaTeamsAsync(client);

        var unchanged = await ReadPlanAsync(await ReorderAsync(client, plan.Revision, plan.Teams.Select(team => team.Id).ToList()));

        Assert.Equal(plan.Revision, unchanged.Revision);
    }

    [Fact]
    public async Task ReorderSetMismatchIs409WithTheCurrentPlan()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await ThreeAlphaTeamsAsync(client);
        var (a, b) = (plan.Teams[0].Id, plan.Teams[1].Id);

        foreach (var teamIds in new List<Guid>[] { [a, b], [a, b, b], [a, b, Guid.NewGuid()] })
        {
            var conflict = await ReadConflictAsync(await ReorderAsync(client, plan.Revision, teamIds));
            Assert.Equal(LegendaryEventPlanIssueCodes.OrderSetMismatch, conflict.IssueCode);
            Assert.Equal(plan.Revision, conflict.Plan.Revision);
            Assert.Equal(["A", "B", "C"], conflict.Plan.Teams.Select(team => team.Name));
        }
    }

    [Fact]
    public async Task StaleRevisionIs409WithTheCurrentPlanAndChangesNothing()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await ThreeAlphaTeamsAsync(client);

        var conflict = await ReadConflictAsync(await client.PostAsJsonAsync(
            $"{PlanUrl}/teams", Team(plan.Revision - 1, "alpha", [AlphaUnits[3]], "D"), Ct));

        Assert.Equal(LegendaryEventPlanIssueCodes.Stale, conflict.IssueCode);
        Assert.Equal(3, conflict.Plan.Revision);
        Assert.Equal(3, conflict.Plan.Teams.Count);
        Assert.Equal(3, (await ReadPlanAsync(await client.GetAsync(PlanUrl, Ct))).Teams.Count);
    }

    [Fact]
    public async Task LazyCreationRequiresRevisionZero()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var conflict = await ReadConflictAsync(await client.PostAsJsonAsync(
            $"{PlanUrl}/teams", Team(1, "alpha", [AlphaUnits[0]], "t"), Ct));

        Assert.Equal(LegendaryEventPlanIssueCodes.Stale, conflict.IssueCode);
        Assert.Equal(0, conflict.Plan.Revision);
        Assert.Empty(conflict.Plan.Teams);
    }

    [Fact]
    public async Task PlansAreScopedToTheCallersProfile()
    {
        var owner = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var other = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await CreateAsync(owner, Team(0, "alpha", [AlphaUnits[0]], "mine"));

        var seen = await ReadPlanAsync(await other.GetAsync(PlanUrl, Ct));
        Assert.Equal(0, seen.Revision);
        Assert.Empty(seen.Teams);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.DeleteAsync($"{PlanUrl}/teams/{plan.Teams[0].Id}?expectedRevision=0", Ct)).StatusCode);
    }

    [Fact]
    public async Task ServedJsonUsesTheContractFieldNames()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        await CreateAsync(client, Team(0, "alpha", [AlphaUnits[0]], "t", depth: 2));

        using var document = JsonDocument.Parse(await client.GetStringAsync(PlanUrl, Ct));
        var root = document.RootElement;
        Assert.Equal(["eventId", "revision", "catalogVersion", "notes", "showPaidOptions", "teams"],
            root.EnumerateObject().Select(property => property.Name));
        var team = root.GetProperty("teams")[0];
        Assert.Equal(["id", "laneId", "name", "sortOrder", "memberUnitIds", "reserveUnitId", "objectiveIndexes", "runDepths"],
            team.EnumerateObject().Select(property => property.Name));
        Assert.Equal(["run", "expectedBattleClears", "expectedBattleClearsSource", "recordedAt"],
            team.GetProperty("runDepths")[0].EnumerateObject().Select(property => property.Name));
    }

    private async Task<LegendaryEventPlanResponse> ThreeAlphaTeamsAsync(HttpClient client)
    {
        var plan = await CreateAsync(client, Team(0, "alpha", [AlphaUnits[0]], "A"));
        plan = await CreateAsync(client, Team(plan.Revision, "alpha", [AlphaUnits[1]], "B"));
        return await CreateAsync(client, Team(plan.Revision, "alpha", [AlphaUnits[2]], "C"));
    }

    private static CreateLegendaryEventTeamRequest Team(
        long revision, string laneId, IReadOnlyList<string> members, string name = "Team",
        string? reserve = null, IReadOnlyList<int>? objectives = null, int run = 1, int? depth = null) =>
        new(revision, laneId, name, members, reserve, objectives ?? [], run, depth, depth is null ? null : "manual");

    private static UpdateLegendaryEventTeamRequest Update(
        long revision, LegendaryEventTeamResponse team, int run = 1, int? depth = null) =>
        new(revision, team.Name, team.MemberUnitIds, team.ReserveUnitId, team.ObjectiveIndexes, run, depth,
            depth is null ? null : "manual");

    private static async Task<LegendaryEventPlanResponse> CreateAsync(HttpClient client, CreateLegendaryEventTeamRequest request) =>
        await ReadPlanAsync(await client.PostAsJsonAsync($"{PlanUrl}/teams", request, Ct));

    private static Task<HttpResponseMessage> UpdateAsync(HttpClient client, Guid teamId, UpdateLegendaryEventTeamRequest request) =>
        client.PutAsJsonAsync($"{PlanUrl}/teams/{teamId}", request, Ct);

    private static Task<HttpResponseMessage> ReorderAsync(HttpClient client, long revision, IReadOnlyList<Guid> teamIds) =>
        client.PutAsJsonAsync($"{PlanUrl}/teams/order", new UpdateLegendaryEventTeamOrderRequest(revision, "alpha", teamIds), Ct);

    private static async Task<LegendaryEventPlanResponse> ReadPlanAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        var plan = await response.Content.ReadFromJsonAsync<LegendaryEventPlanResponse>(Ct);
        Assert.NotNull(plan);
        return plan;
    }

    private static async Task<LegendaryEventPlanConflictResponse> ReadConflictAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var conflict = await response.Content.ReadFromJsonAsync<LegendaryEventPlanConflictResponse>(Ct);
        Assert.NotNull(conflict);
        return conflict;
    }

    private static async Task AssertBadRequestAsync(HttpResponseMessage response, string field)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, body);
        using var document = JsonDocument.Parse(body);
        Assert.Contains(field,
            document.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name),
            StringComparer.OrdinalIgnoreCase);
    }
}
