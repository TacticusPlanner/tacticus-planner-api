using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TacticusPlanner.Api.Features.LegendaryEventPlans;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.Persistence.Interceptors;
using Testcontainers.PostgreSql;
using Xunit;

namespace TacticusPlanner.Persistence.IntegrationTests;

/// <summary>
/// Relational coverage for <c>add-legendary-event-teams</c>: the <c>AddLegendaryEventPlans</c> migration
/// applies on Postgres, rows written by SQL read back through the entity model, the CHECK and unique
/// constraints reject what the API would never write, deleting a profile cascades through plans, teams,
/// members, objectives and run depths, and the writer's revision contract holds under real concurrency
/// (the InMemory API suite cannot exercise save-time conflicts).
/// </summary>
public sealed class LegendaryEventPlansMigrationPostgresTests
{
    private const string EventId = "astarLysander";

    [Fact]
    public async Task MigrationAppliesAndSqlRowsReadBackThroughTheModel()
    {
        await using var postgres = await StartPostgresAsync();
        var (connectionString, profileId) = await MigrateAndSeedProfileAsync(postgres);
        var planId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        await ExecuteAsync(connectionString, """
            INSERT INTO legendary_event_plans (id, revision, profile_id, event_id, catalog_version, notes, show_paid_options, created_at, updated_at)
            VALUES (@plan, 3, @profile, 'astarLysander', 'dev-2026-09-02', 'Alpha first', TRUE, now(), now());
            INSERT INTO legendary_event_teams (id, plan_id, lane_id, name, sort_order, created_at, updated_at)
            VALUES (@team, @plan, 'alpha', 'Melee', 0, now(), now());
            INSERT INTO legendary_event_team_members (team_id, reserve, position, unit_id) VALUES
                (@team, FALSE, 0, 'ultraInceptorSgt'), (@team, FALSE, 1, 'astarCyrus'), (@team, TRUE, 0, 'admecDominus');
            INSERT INTO legendary_event_team_objectives (team_id, objective_index) VALUES (@team, 3), (@team, 0);
            INSERT INTO legendary_event_team_run_depths (team_id, run, expected_battle_clears, source, recorded_at) VALUES
                (@team, 2, 9, 'Manual', now()), (@team, 1, 7, 'Estimate', now());
            """, ("plan", planId), ("profile", profileId.Value), ("team", teamId));

        await using var db = NewContext(connectionString, profileId);
        var response = await new LegendaryEventPlanProjection(db).ReadAsync(EventId, TestContext.Current.CancellationToken);

        Assert.Equal((3, "dev-2026-09-02", "Alpha first", true), (response.Revision, response.CatalogVersion, response.Notes, response.ShowPaidOptions));
        var team = Assert.Single(response.Teams);
        Assert.Equal(teamId, team.Id);
        Assert.Equal(["ultraInceptorSgt", "astarCyrus"], team.MemberUnitIds);
        Assert.Equal("admecDominus", team.ReserveUnitId);
        Assert.Equal([0, 3], team.ObjectiveIndexes);
        Assert.Equal([(1, 7, "estimate"), (2, 9, "manual")],
            team.RunDepths.Select(depth => (depth.Run, depth.ExpectedBattleClears, depth.ExpectedBattleClearsSource)));
    }

    [Theory]
    [InlineData("INSERT INTO legendary_event_team_members (team_id, reserve, position, unit_id) VALUES (@team, FALSE, 1, 'ultraInceptorSgt')", "23505")]
    [InlineData("INSERT INTO legendary_event_team_members (team_id, reserve, position, unit_id) VALUES (@team, FALSE, 5, 'astarCyrus')", "23514")]
    [InlineData("INSERT INTO legendary_event_team_members (team_id, reserve, position, unit_id) VALUES (@team, TRUE, 1, 'astarCyrus')", "23514")]
    [InlineData("INSERT INTO legendary_event_teams (id, plan_id, lane_id, name, sort_order, created_at, updated_at) VALUES (gen_random_uuid(), @plan, 'delta', 'X', 0, now(), now())", "23514")]
    [InlineData("INSERT INTO legendary_event_teams (id, plan_id, lane_id, name, sort_order, created_at, updated_at) VALUES (gen_random_uuid(), @plan, 'beta', 'X', -1, now(), now())", "23514")]
    [InlineData("INSERT INTO legendary_event_team_run_depths (team_id, run, expected_battle_clears, source, recorded_at) VALUES (@team, 4, 5, 'Manual', now())", "23514")]
    [InlineData("INSERT INTO legendary_event_team_run_depths (team_id, run, expected_battle_clears, source, recorded_at) VALUES (@team, 1, 0, 'Manual', now())", "23514")]
    [InlineData("INSERT INTO legendary_event_team_run_depths (team_id, run, expected_battle_clears, source, recorded_at) VALUES (@team, 1, 5, 'Guess', now())", "23514")]
    [InlineData("INSERT INTO legendary_event_team_objectives (team_id, objective_index) VALUES (@team, -1)", "23514")]
    [InlineData("INSERT INTO legendary_event_plans (id, revision, profile_id, event_id, catalog_version, show_paid_options, created_at, updated_at) VALUES (gen_random_uuid(), 1, @profile, 'astarLysander', 'v', FALSE, now(), now())", "23505")]
    public async Task ConstraintsRejectInvalidRows(string sql, string expectedSqlState)
    {
        await using var postgres = await StartPostgresAsync();
        var (connectionString, profileId) = await MigrateAndSeedProfileAsync(postgres);
        var planId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        await ExecuteAsync(connectionString, """
            INSERT INTO legendary_event_plans (id, revision, profile_id, event_id, catalog_version, show_paid_options, created_at, updated_at)
            VALUES (@plan, 1, @profile, 'astarLysander', 'v', FALSE, now(), now());
            INSERT INTO legendary_event_teams (id, plan_id, lane_id, name, sort_order, created_at, updated_at)
            VALUES (@team, @plan, 'alpha', 'Melee', 0, now(), now());
            INSERT INTO legendary_event_team_members (team_id, reserve, position, unit_id) VALUES (@team, FALSE, 0, 'ultraInceptorSgt');
            """, ("plan", planId), ("profile", profileId.Value), ("team", teamId));

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connectionString, sql, ("plan", planId), ("profile", profileId.Value), ("team", teamId)));

        Assert.Equal(expectedSqlState, exception.SqlState);
    }

    [Fact]
    public async Task DeletingTheProfileCascadesThroughEverything()
    {
        await using var postgres = await StartPostgresAsync();
        var (connectionString, profileId) = await MigrateAndSeedProfileAsync(postgres);
        await using (var db = NewContext(connectionString, profileId))
        {
            var writer = NewWriter(db);
            var result = await writer.WriteAsync(profileId, EventId, 0, plan =>
            {
                var team = LegendaryEventPlanWriter.AppendTeam(plan, "alpha", new("A", ["ultraInceptorSgt", "astarCyrus"], "admecDominus", [0, 1]),
                    new(1, 7, LegendaryEventDepthSource.Manual), writer.Now);
                LegendaryEventPlanWriter.AppendTeam(plan, "beta", new("B", ["eldarAutarch"], null, []), null, writer.Now);
                _ = team;
                return LegendaryEventMutationOutcome.Changed;
            }, TestContext.Current.CancellationToken);
            Assert.IsType<LegendaryEventPlanWriteResult.Ok>(result);
        }

        Assert.Equal(2, await CountAsync(connectionString, "legendary_event_teams"));
        await ExecuteAsync(connectionString, "DELETE FROM profiles WHERE id = @profile", ("profile", profileId.Value));

        foreach (var table in new[]
                 {
                     "legendary_event_plans", "legendary_event_teams", "legendary_event_team_members",
                     "legendary_event_team_objectives", "legendary_event_team_run_depths",
                 })
        {
            Assert.Equal(0, await CountAsync(connectionString, table));
        }
    }

    [Fact]
    public async Task ConcurrentFirstWritersLeaveOnePlanAndOneWinner()
    {
        await using var postgres = await StartPostgresAsync();
        var (connectionString, profileId) = await MigrateAndSeedProfileAsync(postgres);

        var results = await Task.WhenAll(
            AppendTeamAsync(connectionString, profileId, 0, "One"),
            AppendTeamAsync(connectionString, profileId, 0, "Two"));

        Assert.Single(results, result => result is LegendaryEventPlanWriteResult.Ok);
        var stale = Assert.Single(results, result => result is LegendaryEventPlanWriteResult.Stale);
        Assert.Equal(1, ((LegendaryEventPlanWriteResult.Stale)stale).Plan.Revision);
        Assert.Equal(1, await CountAsync(connectionString, "legendary_event_plans"));
        Assert.Equal(1, await CountAsync(connectionString, "legendary_event_teams"));
    }

    [Fact]
    public async Task ConcurrentWritersOnAnExistingPlanProduceExactlyOneWinner()
    {
        await using var postgres = await StartPostgresAsync();
        var (connectionString, profileId) = await MigrateAndSeedProfileAsync(postgres);
        Assert.IsType<LegendaryEventPlanWriteResult.Ok>(await AppendTeamAsync(connectionString, profileId, 0, "Seed"));

        var results = await Task.WhenAll(
            AppendTeamAsync(connectionString, profileId, 1, "One"),
            AppendTeamAsync(connectionString, profileId, 1, "Two"));

        var ok = Assert.Single(results, result => result is LegendaryEventPlanWriteResult.Ok);
        Assert.Equal(2, ((LegendaryEventPlanWriteResult.Ok)ok).Plan.Revision);
        var stale = Assert.Single(results, result => result is LegendaryEventPlanWriteResult.Stale);
        Assert.Equal(2, ((LegendaryEventPlanWriteResult.Stale)stale).Plan.Revision);
        Assert.Equal(2, await CountAsync(connectionString, "legendary_event_teams"));
    }

    [Fact]
    public async Task ReorderAndDeleteKeepLaneOrderDenseAndMemberSwapsSurviveTheUniqueIndex()
    {
        await using var postgres = await StartPostgresAsync();
        var (connectionString, profileId) = await MigrateAndSeedProfileAsync(postgres);
        foreach (var (name, revision) in new[] { ("A", 0L), ("B", 1L), ("C", 2L) })
        {
            Assert.IsType<LegendaryEventPlanWriteResult.Ok>(await AppendTeamAsync(connectionString, profileId, revision, name));
        }

        await using var db = NewContext(connectionString, profileId);
        var writer = NewWriter(db);
        var ct = TestContext.Current.CancellationToken;
        var plan = await new LegendaryEventPlanProjection(db).ReadAsync(EventId, ct);
        var ids = plan.Teams.ToDictionary(team => team.Name, team => LegendaryEventTeamId.From(team.Id));

        var reordered = await writer.WriteAsync(profileId, EventId, 3,
            entity => LegendaryEventPlanWriter.Reorder(entity, "alpha", [ids["C"], ids["A"], ids["B"]]), ct);
        var okReorder = Assert.IsType<LegendaryEventPlanWriteResult.Ok>(reordered);
        Assert.Equal([("C", 0), ("A", 1), ("B", 2)], okReorder.Plan.Teams.Select(team => (team.Name, team.SortOrder)));

        var swapped = await writer.WriteAsync(profileId, EventId, 4, entity =>
        {
            var team = entity.Teams.Single(candidate => candidate.Id == ids["A"]);
            LegendaryEventPlanWriter.ApplyContent(team, new("A", ["astarCyrus", "ultraInceptorSgt"], null, [1]));
            return LegendaryEventMutationOutcome.Changed;
        }, ct);
        var okSwap = Assert.IsType<LegendaryEventPlanWriteResult.Ok>(swapped);
        Assert.Equal(["astarCyrus", "ultraInceptorSgt"], okSwap.Plan.Teams.Single(team => team.Name == "A").MemberUnitIds);

        var deleted = await writer.WriteAsync(profileId, EventId, 5, entity => LegendaryEventPlanWriter.RemoveTeam(entity, ids["A"]), ct);
        var okDelete = Assert.IsType<LegendaryEventPlanWriteResult.Ok>(deleted);
        Assert.Equal(6, okDelete.Plan.Revision);
        Assert.Equal([("C", 0), ("B", 1)], okDelete.Plan.Teams.Select(team => (team.Name, team.SortOrder)));
        Assert.Equal(2, await CountAsync(connectionString, "legendary_event_team_members"));
    }

    // ----- Helpers -----

    private static async Task<LegendaryEventPlanWriteResult> AppendTeamAsync(
        string connectionString, ProfileId profileId, long expectedRevision, string name)
    {
        await using var db = NewContext(connectionString, profileId);
        var writer = NewWriter(db);
        return await writer.WriteAsync(profileId, EventId, expectedRevision, plan =>
        {
            LegendaryEventPlanWriter.AppendTeam(plan, "alpha", new(name, ["ultraInceptorSgt", "astarCyrus"], null, [0]), null, writer.Now);
            return LegendaryEventMutationOutcome.Changed;
        }, TestContext.Current.CancellationToken);
    }

    private static LegendaryEventPlanWriter NewWriter(PlannerDbContext db) =>
        new(db, new LegendaryEventPlanProjection(db), TimeProvider.System);

    private static PlannerDbContext NewContext(string connectionString, ProfileId profileId) =>
        new(BuildOptions(connectionString), new PassthroughEncryption(), new StaticProfile(profileId));

    private static async Task<PostgreSqlContainer> StartPostgresAsync()
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        return postgres;
    }

    private static async Task<(string ConnectionString, ProfileId ProfileId)> MigrateAndSeedProfileAsync(PostgreSqlContainer postgres)
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = postgres.GetConnectionString();
        await using (var migrationDb = new PlannerDbContext(BuildOptions(connectionString), new PassthroughEncryption(), new NoProfile()))
        {
            await migrationDb.Database.GetService<IMigrator>().MigrateAsync(cancellationToken: ct);
        }

        var profileId = ProfileId.From(Guid.NewGuid());
        await ExecuteAsync(connectionString, """
            INSERT INTO accounts (id, issuer, subject, created_at, updated_at)
            VALUES (gen_random_uuid(), 'test', 'legendary-event-plans', now(), now());
            INSERT INTO profiles (id, account_id, display_name, created_at, updated_at)
            SELECT @profile, id, 'LRE plans', now(), now() FROM accounts LIMIT 1;
            """, ("profile", profileId.Value));
        return (connectionString, profileId);
    }

    private static async Task ExecuteAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<long> CountAsync(string connectionString, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static DbContextOptions<PlannerDbContext> BuildOptions(string connectionString) =>
        new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(connectionString, options => options.EnableRetryOnFailure())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new EntityMetadataInterceptor(TimeProvider.System))
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

    private sealed class PassthroughEncryption : IColumnEncryptionService
    {
        public string? Encrypt(string? plaintext) => plaintext;

        public string? Decrypt(string? envelope) => envelope;
    }

    private sealed class NoProfile : ICurrentProfileProvider
    {
        public ProfileId? ProfileId => null;
    }

    private sealed class StaticProfile(ProfileId profileId) : ICurrentProfileProvider
    {
        public ProfileId? ProfileId => profileId;
    }
}
