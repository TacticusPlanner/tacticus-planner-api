using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.LegendaryEventPlans;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Tests;

/// <summary>Coverage for <c>legendary-event-plans</c> (openspec change add-legendary-event-teams): the
/// read/empty-plan contract, lazy creation, team create/update/delete/reorder with dense per-lane order,
/// per-run clear depths, catalog validation (400 naming the field), the single plan revision (409 with the
/// current plan) and the served ordering. Uses the embedded catalog's <c>astarLysander</c> event: alpha
/// excludes Xenos, beta excludes Imperial, gamma excludes Chaos; every lane has 18 battles and objective
/// indexes 0–4.</summary>
public sealed class LegendaryEventPlanEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string EventId = "astarLysander";
    private const string Base = $"/api/v1/me/legendary-event-plans/{EventId}";

    // Imperial units: allowed on alpha and gamma.
    private const string U1 = "ultraInceptorSgt";
    private const string U2 = "astarCyrus";
    private const string U3 = "admecDominus";
    private const string U4 = "bloodDante";
    private const string U5 = "custoTrajann";
    private const string U6 = "templHelbrecht";

    // Xenos units: allowed on beta and gamma, never on alpha.
    private const string XenosUnit = "eldarAutarch";
    private const string OtherXenosUnit = "orksBigMek";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ----- Reads -----

    [Fact]
    public async Task AMissingPlanReadsAsAnEmptyPlanWithoutCreatingARow()
    {
        var (client, subject) = await ClientAsync();

        var plan = await GetPlanAsync(client);

        Assert.Equal(EventId, plan.EventId);
        Assert.Equal(0, plan.Revision);
        Assert.Null(plan.Notes);
        Assert.False(plan.ShowPaidOptions);
        Assert.Empty(plan.Teams);
        Assert.False(string.IsNullOrWhiteSpace(plan.CatalogVersion));
        Assert.Equal(0, await CountPlansAsync(subject));
    }

    [Fact]
    public async Task UnknownEventIdIs404OnReadAndWrite()
    {
        var (client, _) = await ClientAsync();

        var read = await client.GetAsync("/api/v1/me/legendary-event-plans/notAnEvent", Ct);
        var write = await client.PutAsJsonAsync(
            "/api/v1/me/legendary-event-plans/notAnEvent", new UpdateLegendaryEventPlanRequest(0, null, false), Ct);
        var team = await client.PostAsJsonAsync(
            "/api/v1/me/legendary-event-plans/notAnEvent/teams", TeamRequest(0), Ct);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, team.StatusCode);
    }

    // ----- Plan-level write -----

    [Fact]
    public async Task PlanLevelWriteCreatesThePlan()
    {
        var (client, _) = await ClientAsync();

        var response = await client.PutAsJsonAsync(Base, new UpdateLegendaryEventPlanRequest(0, "Alpha first", true), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var plan = await ReadAsync<LegendaryEventPlanResponse>(response);
        Assert.Equal(1, plan.Revision);
        Assert.Equal("Alpha first", plan.Notes);
        Assert.True(plan.ShowPaidOptions);
        Assert.Equal(plan.CatalogVersion, (await GetPlanAsync(client)).CatalogVersion);
    }

    [Fact]
    public async Task OverLongNotesAre400NamingNotes()
    {
        var (client, _) = await ClientAsync();

        var response = await client.PutAsJsonAsync(
            Base, new UpdateLegendaryEventPlanRequest(0, new string('x', 2001), false), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("notes", await ErrorKeysAsync(response), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0, (await GetPlanAsync(client)).Revision);
    }

    // ----- Team create -----

    [Fact]
    public async Task CreatingATeamOnAFreshPlanLazilyCreatesIt()
    {
        var (client, _) = await ClientAsync();

        var response = await client.PostAsJsonAsync(Base + "/teams", TeamRequest(
            0, "Melee", [U1, U2, U3], U4, [0, 3], 1, 7, "manual"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var plan = await ReadAsync<LegendaryEventPlanResponse>(response);
        Assert.Equal(1, plan.Revision);
        var team = Assert.Single(plan.Teams);
        Assert.NotEqual(Guid.Empty, team.Id);
        Assert.Equal("alpha", team.LaneId);
        Assert.Equal("Melee", team.Name);
        Assert.Equal(0, team.SortOrder);
        Assert.Equal([U1, U2, U3], team.MemberUnitIds);
        Assert.Equal(U4, team.ReserveUnitId);
        Assert.Equal([0, 3], team.ObjectiveIndexes);
        var depth = Assert.Single(team.RunDepths);
        Assert.Equal(1, depth.Run);
        Assert.Equal(7, depth.ExpectedBattleClears);
        Assert.Equal("manual", depth.ExpectedBattleClearsSource);
    }

    [Fact]
    public async Task ATeamIsAppendedAfterTheLanesExistingTeams()
    {
        var (client, _) = await ClientAsync();
        await CreateTeamAsync(client, 0, "A", [U1]);
        await CreateTeamAsync(client, 1, "B", [U2]);

        var plan = await CreateTeamAsync(client, 2, "C", [U3]);

        Assert.Equal(3, plan.Revision);
        Assert.Equal(["A", "B", "C"], plan.Teams.Select(team => team.Name));
        Assert.Equal([0, 1, 2], plan.Teams.Select(team => team.SortOrder));
    }

    [Fact]
    public async Task MembersKeepTheirPositionOrder()
    {
        var (client, _) = await ClientAsync();

        await CreateTeamAsync(client, 0, "Order", [U3, U1, U2]);

        Assert.Equal([U3, U1, U2], Assert.Single((await GetPlanAsync(client)).Teams).MemberUnitIds);
    }

    [Fact]
    public async Task ATeamMayCoverZeroObjectivesAndHaveNoDepth()
    {
        var (client, _) = await ClientAsync();

        var plan = await CreateTeamAsync(client, 0, "Bare", [U1]);

        var team = Assert.Single(plan.Teams);
        Assert.Empty(team.ObjectiveIndexes);
        Assert.Empty(team.RunDepths);
        Assert.Null(team.ReserveUnitId);
    }

    // ----- Team update -----

    [Fact]
    public async Task UpdatingATeamReplacesItsContentButKeepsLaneAndOrder()
    {
        var (client, _) = await ClientAsync();
        await CreateTeamAsync(client, 0, "First", [U1]);
        var created = await CreateTeamAsync(client, 1, "Second", [U2, U3], objectives: [1]);
        var target = created.Teams.Single(team => team.Name == "Second");

        var response = await client.PutAsJsonAsync($"{Base}/teams/{target.Id}", new UpdateLegendaryEventTeamRequest(
            2, "Renamed", [U4, U5], U6, [2, 4], 1, 9, "estimate"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var plan = await ReadAsync<LegendaryEventPlanResponse>(response);
        Assert.Equal(3, plan.Revision);
        var updated = plan.Teams.Single(team => team.Id == target.Id);
        Assert.Equal("alpha", updated.LaneId);
        Assert.Equal(1, updated.SortOrder);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal([U4, U5], updated.MemberUnitIds);
        Assert.Equal(U6, updated.ReserveUnitId);
        Assert.Equal([2, 4], updated.ObjectiveIndexes);
        var depth = Assert.Single(updated.RunDepths);
        Assert.Equal((1, 9, "estimate"), (depth.Run, depth.ExpectedBattleClears, depth.ExpectedBattleClearsSource));
    }

    [Fact]
    public async Task SwappingMemberOrderOnUpdateSucceeds()
    {
        // Members are stored under a unique (team, unit) index; a position swap must not trip it.
        var (client, _) = await ClientAsync();
        var created = await CreateTeamAsync(client, 0, "Swap", [U1, U2, U3]);
        var team = Assert.Single(created.Teams);

        var plan = await UpdateTeamAsync(client, team.Id, new UpdateLegendaryEventTeamRequest(1, "Swap", [U3, U2, U1], null, []));

        Assert.Equal([U3, U2, U1], Assert.Single(plan.Teams).MemberUnitIds);
    }

    [Fact]
    public async Task ADepthWriteTouchesOnlyItsRun()
    {
        var (client, _) = await ClientAsync();
        var created = await CreateTeamAsync(client, 0, "Depths", [U1], depth: (1, 7, "manual"));
        var team = Assert.Single(created.Teams);

        var plan = await UpdateTeamAsync(client, team.Id, new UpdateLegendaryEventTeamRequest(1, "Depths", [U1], null, [], 2, 9, "manual"));

        var depths = Assert.Single(plan.Teams).RunDepths;
        Assert.Equal([(1, 7, "manual"), (2, 9, "manual")],
            depths.Select(depth => (depth.Run, depth.ExpectedBattleClears, depth.ExpectedBattleClearsSource)));
    }

    [Fact]
    public async Task ANullDepthClearsOnlyItsRun()
    {
        var (client, _) = await ClientAsync();
        var created = await CreateTeamAsync(client, 0, "Depths", [U1], depth: (1, 7, "manual"));
        var team = Assert.Single(created.Teams);
        await UpdateTeamAsync(client, team.Id, new UpdateLegendaryEventTeamRequest(1, "Depths", [U1], null, [], 2, 9, "manual"));

        var plan = await UpdateTeamAsync(client, team.Id, new UpdateLegendaryEventTeamRequest(2, "Depths", [U1], null, [], 2, null, null));

        var depth = Assert.Single(Assert.Single(plan.Teams).RunDepths);
        Assert.Equal((1, 7), (depth.Run, depth.ExpectedBattleClears));
    }

    [Fact]
    public async Task RewritingARunsDepthRefreshesItsRecordedAt()
    {
        var (client, _) = await ClientAsync();
        var created = await CreateTeamAsync(client, 0, "Depths", [U1], depth: (1, 7, "manual"));
        var team = Assert.Single(created.Teams);
        var before = Assert.Single(team.RunDepths).RecordedAt;

        var plan = await UpdateTeamAsync(client, team.Id, new UpdateLegendaryEventTeamRequest(1, "Depths", [U1], null, [], 1, 8, "estimate"));

        var depth = Assert.Single(Assert.Single(plan.Teams).RunDepths);
        Assert.Equal((8, "estimate"), (depth.ExpectedBattleClears, depth.ExpectedBattleClearsSource));
        Assert.True(depth.RecordedAt >= before);
    }

    [Fact]
    public async Task LaneCannotChangeOnUpdate()
    {
        var (client, _) = await ClientAsync();
        var created = await CreateTeamAsync(client, 0, "Alpha", [U1]);
        var team = Assert.Single(created.Teams);

        var response = await client.PutAsJsonAsync($"{Base}/teams/{team.Id}",
            new UpdateLegendaryEventTeamRequest(1, "Alpha", [U1], null, [], LaneId: "beta"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("laneId", await ErrorKeysAsync(response), StringComparer.OrdinalIgnoreCase);
        Assert.Equal("alpha", Assert.Single((await GetPlanAsync(client)).Teams).LaneId);
    }

    [Fact]
    public async Task UpdateIsValidatedAgainstTheTeamsOwnLane()
    {
        var (client, _) = await ClientAsync();
        var created = await CreateTeamAsync(client, 0, "Alpha", [U1]);
        var team = Assert.Single(created.Teams);

        var response = await client.PutAsJsonAsync($"{Base}/teams/{team.Id}",
            new UpdateLegendaryEventTeamRequest(1, "Alpha", [XenosUnit], null, []), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("memberUnitIds", await ErrorKeysAsync(response), StringComparer.OrdinalIgnoreCase);
        var plan = await GetPlanAsync(client);
        Assert.Equal(1, plan.Revision);
        Assert.Equal([U1], Assert.Single(plan.Teams).MemberUnitIds);
    }

    [Fact]
    public async Task UnknownTeamIdIs404OnUpdateAndDelete()
    {
        var (client, _) = await ClientAsync();
        await CreateTeamAsync(client, 0, "Alpha", [U1]);

        var update = await client.PutAsJsonAsync($"{Base}/teams/{Guid.NewGuid()}",
            new UpdateLegendaryEventTeamRequest(1, "Alpha", [U1], null, []), Ct);
        var delete = await client.DeleteAsync($"{Base}/teams/{Guid.NewGuid()}?expectedRevision=1", Ct);

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(1, (await GetPlanAsync(client)).Revision);
    }

    [Fact]
    public async Task AnotherProfilesTeamIsNotFound()
    {
        var (owner, _) = await ClientAsync();
        var (other, _) = await ClientAsync();
        var created = await CreateTeamAsync(owner, 0, "Mine", [U1]);
        var team = Assert.Single(created.Teams);

        var response = await other.PutAsJsonAsync($"{Base}/teams/{team.Id}",
            new UpdateLegendaryEventTeamRequest(0, "Stolen", [U1], null, []), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty((await GetPlanAsync(other)).Teams);
        Assert.Equal("Mine", Assert.Single((await GetPlanAsync(owner)).Teams).Name);
    }

    // ----- Team delete -----

    [Fact]
    public async Task DeletingATeamReDensifiesItsLane()
    {
        var (client, _) = await ClientAsync();
        await CreateTeamAsync(client, 0, "A", [U1]);
        var withB = await CreateTeamAsync(client, 1, "B", [U2]);
        await CreateTeamAsync(client, 2, "C", [U3]);
        var b = withB.Teams.Single(team => team.Name == "B");

        var response = await client.DeleteAsync($"{Base}/teams/{b.Id}?expectedRevision=3", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var plan = await ReadAsync<LegendaryEventPlanResponse>(response);
        Assert.Equal(4, plan.Revision);
        Assert.Equal([("A", 0), ("C", 1)], plan.Teams.Select(team => (team.Name, team.SortOrder)));
    }

    // ----- Catalog validation -----

    [Fact]
    public async Task UnitNotAllowedOnTheLaneIs400NamingMemberUnitIds()
    {
        var (client, _) = await ClientAsync();

        var response = await client.PostAsJsonAsync(Base + "/teams", TeamRequest(0, "Bad", [U1, XenosUnit]), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("memberUnitIds", await ErrorKeysAsync(response), StringComparer.OrdinalIgnoreCase);
        Assert.Empty((await GetPlanAsync(client)).Teams);
    }

    [Fact]
    public async Task ReserveNotAllowedOnTheLaneIs400NamingReserveUnitId()
    {
        var (client, _) = await ClientAsync();

        var response = await client.PostAsJsonAsync(Base + "/teams", TeamRequest(0, "Bad", [U1], reserve: XenosUnit), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("reserveUnitId", await ErrorKeysAsync(response), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RepeatedUnitAcrossMembersAndReserveIs400()
    {
        var (client, _) = await ClientAsync();

        var members = await client.PostAsJsonAsync(Base + "/teams", TeamRequest(0, "Dup", [U1, U1]), Ct);
        var reserve = await client.PostAsJsonAsync(Base + "/teams", TeamRequest(0, "Dup", [U1], reserve: U1), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, members.StatusCode);
        Assert.Contains("memberUnitIds", await ErrorKeysAsync(members), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, reserve.StatusCode);
        Assert.Contains("reserveUnitId", await ErrorKeysAsync(reserve), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ObjectiveIndexOutsideTheLaneIs400()
    {
        var (client, _) = await ClientAsync();

        var outside = await client.PostAsJsonAsync(Base + "/teams", TeamRequest(0, "Obj", [U1], objectives: [5]), Ct);
        var repeated = await client.PostAsJsonAsync(Base + "/teams", TeamRequest(0, "Obj", [U1], objectives: [1, 1]), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode);
        Assert.Contains("objectiveIndexes", await ErrorKeysAsync(outside), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, repeated.StatusCode);
        Assert.Contains("objectiveIndexes", await ErrorKeysAsync(repeated), StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1, 19, "manual", "expectedBattleClears")]
    [InlineData(1, 0, "manual", "expectedBattleClears")]
    [InlineData(4, 5, "manual", "run")]
    [InlineData(0, 5, "manual", "run")]
    [InlineData(1, 5, null, "expectedBattleClearsSource")]
    [InlineData(1, null, "manual", "expectedBattleClearsSource")]
    [InlineData(1, 5, "guess", "expectedBattleClearsSource")]
    public async Task InvalidDepthsAre400NamingTheField(int run, int? clears, string? source, string field)
    {
        var (client, _) = await ClientAsync();

        var response = await client.PostAsJsonAsync(Base + "/teams", TeamRequest(0, "Depth", [U1], run: run, clears: clears, source: source), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await ErrorKeysAsync(response), StringComparer.OrdinalIgnoreCase);
        Assert.Empty((await GetPlanAsync(client)).Teams);
    }

    [Theory]
    [InlineData("delta")]
    [InlineData("")]
    [InlineData(null)]
    public async Task UnknownLaneIs400NamingLaneId(string? lane)
    {
        var (client, _) = await ClientAsync();

        var response = await client.PostAsJsonAsync(Base + "/teams", TeamRequest(0, "Lane", [U1], lane: lane), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("laneId", await ErrorKeysAsync(response), StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyOrOverLongNameIs400(string name)
    {
        var (client, _) = await ClientAsync();

        var empty = await client.PostAsJsonAsync(Base + "/teams", TeamRequest(0, name, [U1]), Ct);
        var overLong = await client.PostAsJsonAsync(Base + "/teams", TeamRequest(0, new string('n', 61), [U1]), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains("name", await ErrorKeysAsync(empty), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, overLong.StatusCode);
        Assert.Contains("name", await ErrorKeysAsync(overLong), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyOrOversizedMemberListIs400()
    {
        var (client, _) = await ClientAsync();

        var empty = await client.PostAsJsonAsync(Base + "/teams", TeamRequest(0, "Size", []), Ct);
        var six = await client.PostAsJsonAsync(Base + "/teams", TeamRequest(0, "Size", [U1, U2, U3, U4, U5, U6]), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains("memberUnitIds", await ErrorKeysAsync(empty), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, six.StatusCode);
        Assert.Contains("memberUnitIds", await ErrorKeysAsync(six), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EveryWriteStampsTheCurrentCatalogVersion()
    {
        var (client, subject) = await ClientAsync();
        await CreateTeamAsync(client, 0, "A", [U1]);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().Include(entity => entity.Profile)
            .FirstAsync(entity => entity.Subject == subject, Ct);
        var stored = await db.LegendaryEventPlans.IgnoreQueryFilters()
            .SingleAsync(plan => plan.ProfileId == account.Profile!.Id && plan.EventId == EventId, Ct);
        Assert.Equal((await GetPlanAsync(client)).CatalogVersion, stored.CatalogVersion);
    }

    [Fact]
    public async Task ReadsNeverFailOnCatalogDrift()
    {
        // A plan persisted under an older catalog whose unit and objective no longer validate still reads.
        var (client, subject) = await ClientAsync();
        await CreateTeamAsync(client, 0, "Old", [U1], objectives: [0]);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
            var account = await db.Accounts.IgnoreQueryFilters().Include(entity => entity.Profile)
                .FirstAsync(entity => entity.Subject == subject, Ct);
            var plan = await db.LegendaryEventPlans.IgnoreQueryFilters()
                .Include(entity => entity.Teams).ThenInclude(team => team.Members)
                .Include(entity => entity.Teams).ThenInclude(team => team.Objectives)
                .SingleAsync(entity => entity.ProfileId == account.Profile!.Id && entity.EventId == EventId, Ct);
            plan.CatalogVersion = "dev-2000-01-01";
            var team = plan.Teams.Single();
            team.Members.Single().UnitId = "retiredUnit";
            db.LegendaryEventTeamObjectives.Remove(team.Objectives.Single());
            db.LegendaryEventTeamObjectives.Add(new Domain.LegendaryEvents.LegendaryEventTeamObjective { TeamId = team.Id, ObjectiveIndex = 42 });
            await db.SaveChangesAsync(Ct);
        }

        var read = await GetPlanAsync(client);

        Assert.Equal("dev-2000-01-01", read.CatalogVersion);
        var stored = Assert.Single(read.Teams);
        Assert.Equal(["retiredUnit"], stored.MemberUnitIds);
        Assert.Equal([42], stored.ObjectiveIndexes);
    }

    // ----- Reorder -----

    [Fact]
    public async Task ReorderingALaneAssignsDenseOrderAndBumpsTheRevision()
    {
        var (client, _) = await ClientAsync();
        await CreateTeamAsync(client, 0, "A", [U1]);
        await CreateTeamAsync(client, 1, "B", [U2]);
        var created = await CreateTeamAsync(client, 2, "C", [U3]);
        var ids = created.Teams.ToDictionary(team => team.Name, team => team.Id);

        var response = await client.PutAsJsonAsync(Base + "/teams/order",
            new UpdateLegendaryEventTeamOrderRequest(3, "alpha", [ids["C"], ids["A"], ids["B"]]), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var plan = await ReadAsync<LegendaryEventPlanResponse>(response);
        Assert.Equal(4, plan.Revision);
        Assert.Equal([("C", 0), ("A", 1), ("B", 2)], plan.Teams.Select(team => (team.Name, team.SortOrder)));
    }

    [Fact]
    public async Task ReorderSetMismatchIs409WithTheCurrentPlan()
    {
        var (client, _) = await ClientAsync();
        await CreateTeamAsync(client, 0, "A", [U1]);
        await CreateTeamAsync(client, 1, "B", [U2]);
        var created = await CreateTeamAsync(client, 2, "C", [U3]);
        var ids = created.Teams.ToDictionary(team => team.Name, team => team.Id);

        var missing = await client.PutAsJsonAsync(Base + "/teams/order",
            new UpdateLegendaryEventTeamOrderRequest(3, "alpha", [ids["A"], ids["B"]]), Ct);
        var duplicated = await client.PutAsJsonAsync(Base + "/teams/order",
            new UpdateLegendaryEventTeamOrderRequest(3, "alpha", [ids["A"], ids["A"], ids["B"]]), Ct);
        var foreign = await client.PutAsJsonAsync(Base + "/teams/order",
            new UpdateLegendaryEventTeamOrderRequest(3, "alpha", [ids["A"], ids["B"], Guid.NewGuid()]), Ct);

        foreach (var response in new[] { missing, duplicated, foreign })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var conflict = await ReadAsync<LegendaryEventPlanConflictResponse>(response);
            Assert.Equal("legendaryEventOrderSetMismatch", conflict.IssueCode);
            Assert.Equal(3, conflict.Plan.Revision);
        }

        Assert.Equal([("A", 0), ("B", 1), ("C", 2)], (await GetPlanAsync(client)).Teams.Select(team => (team.Name, team.SortOrder)));
    }

    [Fact]
    public async Task UnchangedOrderKeepsTheRevision()
    {
        var (client, _) = await ClientAsync();
        await CreateTeamAsync(client, 0, "A", [U1]);
        var created = await CreateTeamAsync(client, 1, "B", [U2]);
        var ids = created.Teams.Select(team => team.Id).ToList();

        var response = await client.PutAsJsonAsync(Base + "/teams/order",
            new UpdateLegendaryEventTeamOrderRequest(2, "alpha", ids), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await ReadAsync<LegendaryEventPlanResponse>(response)).Revision);
    }

    [Fact]
    public async Task ReorderingAnEmptyLaneWithAnEmptySetSucceedsUnchanged()
    {
        var (client, _) = await ClientAsync();
        await CreateTeamAsync(client, 0, "A", [U1]);

        var response = await client.PutAsJsonAsync(Base + "/teams/order",
            new UpdateLegendaryEventTeamOrderRequest(1, "beta", []), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await ReadAsync<LegendaryEventPlanResponse>(response)).Revision);
    }

    // ----- Revision guard -----

    [Fact]
    public async Task StaleRevisionIs409WithTheCurrentPlanAndChangesNothing()
    {
        var (client, _) = await ClientAsync();
        await CreateTeamAsync(client, 0, "A", [U1]);
        await CreateTeamAsync(client, 1, "B", [U2]);
        var current = await CreateTeamAsync(client, 2, "C", [U3]);
        var c = current.Teams.Single(team => team.Name == "C");

        var create = await client.PostAsJsonAsync(Base + "/teams", TeamRequest(2, "D", [U4]), Ct);
        var update = await client.PutAsJsonAsync($"{Base}/teams/{c.Id}", new UpdateLegendaryEventTeamRequest(2, "Z", [U4], null, []), Ct);
        var delete = await client.DeleteAsync($"{Base}/teams/{c.Id}?expectedRevision=2", Ct);
        var plan = await client.PutAsJsonAsync(Base, new UpdateLegendaryEventPlanRequest(2, "n", true), Ct);
        var order = await client.PutAsJsonAsync(Base + "/teams/order",
            new UpdateLegendaryEventTeamOrderRequest(2, "alpha", current.Teams.Select(team => team.Id).Reverse().ToList()), Ct);

        foreach (var response in new[] { create, update, delete, plan, order })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var conflict = await ReadAsync<LegendaryEventPlanConflictResponse>(response);
            Assert.Equal("legendaryEventPlanStale", conflict.IssueCode);
            Assert.Equal(3, conflict.Plan.Revision);
            Assert.Equal(3, conflict.Plan.Teams.Count);
        }

        var after = await GetPlanAsync(client);
        Assert.Equal(3, after.Revision);
        Assert.Null(after.Notes);
        Assert.Equal(["A", "B", "C"], after.Teams.Select(team => team.Name));
    }

    [Fact]
    public async Task LazyCreationRequiresRevisionZero()
    {
        var (client, subject) = await ClientAsync();

        var response = await client.PostAsJsonAsync(Base + "/teams", TeamRequest(1, "A", [U1]), Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var conflict = await ReadAsync<LegendaryEventPlanConflictResponse>(response);
        Assert.Equal("legendaryEventPlanStale", conflict.IssueCode);
        Assert.Equal(0, conflict.Plan.Revision);
        Assert.Empty(conflict.Plan.Teams);
        Assert.Equal(0, await CountPlansAsync(subject));
    }

    // ----- Served shape -----

    [Fact]
    public async Task TeamsAreServedByLaneThenSortOrder()
    {
        var (client, _) = await ClientAsync();
        await CreateTeamAsync(client, 0, "G0", [U1], lane: "gamma");
        await CreateTeamAsync(client, 1, "A0", [U1], lane: "alpha");
        await CreateTeamAsync(client, 2, "A1", [U2], lane: "alpha");
        var created = await CreateTeamAsync(client, 3, "B0", [XenosUnit], lane: "beta");
        var alpha = created.Teams.Where(team => team.LaneId == "alpha").ToDictionary(team => team.Name, team => team.Id);
        var reordered = await client.PutAsJsonAsync(Base + "/teams/order",
            new UpdateLegendaryEventTeamOrderRequest(4, "alpha", [alpha["A1"], alpha["A0"]]), Ct);
        reordered.EnsureSuccessStatusCode();

        var plan = await GetPlanAsync(client);

        Assert.Equal(
            [("alpha", 0, "A1"), ("alpha", 1, "A0"), ("beta", 0, "B0"), ("gamma", 0, "G0")],
            plan.Teams.Select(team => (team.LaneId, team.SortOrder, team.Name)));
    }

    [Fact]
    public async Task PlansAreScopedToTheCallersProfile()
    {
        var (first, _) = await ClientAsync();
        var (second, _) = await ClientAsync();
        await CreateTeamAsync(first, 0, "Mine", [U1]);

        var other = await GetPlanAsync(second);

        Assert.Equal(0, other.Revision);
        Assert.Empty(other.Teams);
    }

    [Fact]
    public async Task EachLaneValidatesAgainstItsOwnAllowedUnits()
    {
        var (client, _) = await ClientAsync();

        var beta = await client.PostAsJsonAsync(Base + "/teams", TeamRequest(0, "Beta", [XenosUnit, OtherXenosUnit], lane: "beta"), Ct);
        var betaImperial = await client.PostAsJsonAsync(Base + "/teams", TeamRequest(1, "Beta", [U1], lane: "beta"), Ct);

        Assert.Equal(HttpStatusCode.OK, beta.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, betaImperial.StatusCode);
    }

    // ----- Helpers -----

    private async Task<(HttpClient Client, string Subject)> ClientAsync()
    {
        var subject = $"lre-plans-{Guid.NewGuid()}";
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory, subject);
        return (client, subject);
    }

    private async Task<int> CountPlansAsync(string subject)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().Include(entity => entity.Profile)
            .FirstAsync(entity => entity.Subject == subject, Ct);
        return await db.LegendaryEventPlans.IgnoreQueryFilters().CountAsync(plan => plan.ProfileId == account.Profile!.Id, Ct);
    }

    private static CreateLegendaryEventTeamRequest TeamRequest(
        long revision,
        string? name = "Team",
        List<string>? members = null,
        string? reserve = null,
        List<int>? objectives = null,
        int run = 1,
        int? clears = null,
        string? source = null,
        string? lane = "alpha") =>
        new(revision, lane, name, members ?? [U1], reserve, objectives ?? [], run, clears, source);

    private static async Task<LegendaryEventPlanResponse> CreateTeamAsync(
        HttpClient client,
        long revision,
        string name,
        List<string> members,
        string lane = "alpha",
        List<int>? objectives = null,
        (int Run, int Clears, string Source)? depth = null)
    {
        var response = await client.PostAsJsonAsync(Base + "/teams", TeamRequest(
            revision, name, members, null, objectives, depth?.Run ?? 1, depth?.Clears, depth?.Source, lane), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<LegendaryEventPlanResponse>(response);
    }

    private static async Task<LegendaryEventPlanResponse> UpdateTeamAsync(
        HttpClient client, Guid teamId, UpdateLegendaryEventTeamRequest request)
    {
        var response = await client.PutAsJsonAsync($"{Base}/teams/{teamId}", request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<LegendaryEventPlanResponse>(response);
    }

    private static async Task<LegendaryEventPlanResponse> GetPlanAsync(HttpClient client)
    {
        var response = await client.GetAsync(Base, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<LegendaryEventPlanResponse>(response);
    }

    private static async Task<List<string>> ErrorKeysAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return document.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name).ToList();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<T>(Ct);
        Assert.NotNull(body);
        return body;
    }
}
