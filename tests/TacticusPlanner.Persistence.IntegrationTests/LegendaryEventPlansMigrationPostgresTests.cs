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
/// Relational coverage for <c>AddLegendaryEventPlans</c> (LRE Stage 2): the CHECK and unique constraints the
/// InMemory API tests cannot exercise, the cascade from account purge, the plan-revision 409 when two writers
/// race (including two lazy creators), and dense lane order after a permutation.
/// </summary>
public sealed class LegendaryEventPlansMigrationPostgresTests
{
    private const string EventId = "astarLysander";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConstraintsRejectBadRowsAndAccountPurgeCascades()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(Ct);
        var connectionString = postgres.GetConnectionString();
        var (_, profileId) = await MigrateAndSeedProfileAsync(connectionString);
        var planId = Guid.NewGuid();
        var teamId = Guid.NewGuid();

        await ExecuteAsync(connectionString, """
            INSERT INTO legendary_event_plans (id, revision, profile_id, event_id, catalog_version, show_paid_options, created_at, updated_at)
            VALUES (@plan, 1, @profile, 'astarLysander', 'v', FALSE, now(), now());
            INSERT INTO legendary_event_teams (id, plan_id, lane_id, name, sort_order, created_at, updated_at)
            VALUES (@team, @plan, 'alpha', 'Melee', 0, now(), now());
            INSERT INTO legendary_event_team_members (team_id, reserve, position, unit_id) VALUES
                (@team, FALSE, 0, 'u1'), (@team, FALSE, 1, 'u2'), (@team, TRUE, 0, 'u3');
            INSERT INTO legendary_event_team_objectives (team_id, objective_index) VALUES (@team, 0), (@team, 3);
            INSERT INTO legendary_event_team_run_depths (team_id, run, expected_battle_clears, source, recorded_at)
            VALUES (@team, 1, 7, 'Manual', now()), (@team, 2, 9, 'Estimate', now());
            """, ("plan", planId), ("team", teamId), ("profile", profileId.Value));

        // Each rejected statement names the constraint that rejects it.
        string[][] rejected =
        [
            ["ix_legendary_event_team_members_team_id_unit_id",
                "INSERT INTO legendary_event_team_members (team_id, reserve, position, unit_id) VALUES (@team, FALSE, 2, 'u1')"],
            ["ck_legendary_event_team_members_position",
                "INSERT INTO legendary_event_team_members (team_id, reserve, position, unit_id) VALUES (@team, FALSE, 5, 'u9')"],
            ["ck_legendary_event_team_members_position",
                "INSERT INTO legendary_event_team_members (team_id, reserve, position, unit_id) VALUES (@team, TRUE, 1, 'u9')"],
            ["ck_legendary_event_teams_lane_id",
                "INSERT INTO legendary_event_teams (id, plan_id, lane_id, name, sort_order, created_at, updated_at) VALUES (gen_random_uuid(), @plan, 'delta', 'x', 0, now(), now())"],
            ["ck_legendary_event_team_run_depths_run",
                "INSERT INTO legendary_event_team_run_depths (team_id, run, expected_battle_clears, source, recorded_at) VALUES (@team, 4, 1, 'Manual', now())"],
            ["ck_legendary_event_team_run_depths_depth",
                "INSERT INTO legendary_event_team_run_depths (team_id, run, expected_battle_clears, source, recorded_at) VALUES (@team, 3, 0, 'Manual', now())"],
            ["ck_legendary_event_team_run_depths_source",
                "INSERT INTO legendary_event_team_run_depths (team_id, run, expected_battle_clears, source, recorded_at) VALUES (@team, 3, 1, 'Guess', now())"],
            ["ix_legendary_event_plans_profile_id_event_id",
                "INSERT INTO legendary_event_plans (id, revision, profile_id, event_id, catalog_version, show_paid_options, created_at, updated_at) VALUES (gen_random_uuid(), 1, @profile, 'astarLysander', 'v', FALSE, now(), now())"],
        ];
        foreach (var (constraint, sql) in rejected.Select(entry => (entry[0], entry[1])))
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
                connectionString, sql, ("plan", planId), ("team", teamId), ("profile", profileId.Value)));
            Assert.Equal(constraint, exception.ConstraintName);
        }

        await ExecuteAsync(connectionString, "DELETE FROM accounts");

        foreach (var table in new[] { "legendary_event_plans", "legendary_event_teams", "legendary_event_team_members",
                     "legendary_event_team_objectives", "legendary_event_team_run_depths" })
        {
            Assert.Equal(0L, await CountAsync(connectionString, table));
        }
    }

    [Fact]
    public async Task ConcurrentWritersAtTheSameRevisionProduceOneWinnerAndAStale409()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(Ct);
        var (options, profileId) = await MigrateAndSeedProfileAsync(postgres.GetConnectionString());
        var seeded = await WriteAsync(options, profileId, 0, Create("alpha", "u1"));
        var revision = Assert.IsType<LegendaryEventPlanWriteResult.Saved>(seeded).Plan.Revision;

        var (winner, loser) = await RaceAsync(options, profileId, revision);

        Assert.Equal(revision + 1, Assert.IsType<LegendaryEventPlanWriteResult.Saved>(winner).Plan.Revision);
        var conflict = Assert.IsType<LegendaryEventPlanWriteResult.Conflict>(loser).Body;
        Assert.Equal(LegendaryEventPlanIssueCodes.Stale, conflict.IssueCode);
        Assert.Equal(revision + 1, conflict.Plan.Revision);
        Assert.Equal(2, conflict.Plan.Teams.Count);
    }

    [Fact]
    public async Task ConcurrentFirstWritersCreateOnePlan()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(Ct);
        var (options, profileId) = await MigrateAndSeedProfileAsync(postgres.GetConnectionString());

        var (winner, loser) = await RaceAsync(options, profileId, 0);

        Assert.Equal(1, Assert.IsType<LegendaryEventPlanWriteResult.Saved>(winner).Plan.Revision);
        var conflict = Assert.IsType<LegendaryEventPlanWriteResult.Conflict>(loser).Body;
        Assert.Equal(LegendaryEventPlanIssueCodes.Stale, conflict.IssueCode);
        Assert.Equal(1, conflict.Plan.Revision);
        Assert.Equal(1L, await CountAsync(postgres.GetConnectionString(), "legendary_event_plans"));
    }

    [Fact]
    public async Task UpdatesThatSwapUnitsAndReordersKeepRowsConsistent()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(Ct);
        var connectionString = postgres.GetConnectionString();
        var (options, profileId) = await MigrateAndSeedProfileAsync(connectionString);
        var plan = Saved(await WriteAsync(options, profileId, 0, Create("alpha", "u1", "u2", "u3")));
        plan = Saved(await WriteAsync(options, profileId, plan.Revision, Create("alpha", "u4")));
        plan = Saved(await WriteAsync(options, profileId, plan.Revision, Create("alpha", "u5")));
        var ids = plan.Teams.Select(team => team.Id).ToList();

        // Swap units between positions (unique (team_id, unit_id) must not trip mid-save).
        var first = LegendaryEventTeamId.From(ids[0]);
        plan = Saved(await WriteAsync(options, profileId, plan.Revision, (context, ct) =>
            LegendaryEventTeamMutations.UpdateTeamAsync(context, first, Fields("u3", "u1", "u2") with { ReserveUnitId = "u6" }, ct)));
        Assert.Equal(["u3", "u1", "u2"], plan.Teams[0].MemberUnitIds);

        plan = Saved(await WriteAsync(options, profileId, plan.Revision, (context, _) =>
            Task.FromResult(LegendaryEventTeamMutations.ReorderLane(context.Plan, "alpha", [ids[2], ids[0], ids[1]]))));
        plan = Saved(await WriteAsync(options, profileId, plan.Revision, (context, _) =>
            Task.FromResult(LegendaryEventTeamMutations.DeleteTeam(context, first))));

        Assert.Equal([ids[2], ids[1]], plan.Teams.Select(team => team.Id));
        Assert.Equal([0, 1], plan.Teams.Select(team => team.SortOrder));
        Assert.Equal(6, plan.Revision);
        Assert.Equal(2L, await CountAsync(connectionString, "legendary_event_team_members"));
    }

    /// <summary>Two writers load the plan at <paramref name="revision"/>; the second commits while the first is
    /// paused inside its mutation, so the first only learns of it at save time.</summary>
    private static async Task<(LegendaryEventPlanWriteResult Winner, LegendaryEventPlanWriteResult Loser)> RaceAsync(
        DbContextOptions<PlannerDbContext> options, ProfileId profileId, long revision)
    {
        var loaded = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var loserTask = WriteAsync(options, profileId, revision, async (context, ct) =>
        {
            loaded.SetResult();
            await release.Task.WaitAsync(ct);
            return await Create("alpha", "loser")(context, ct);
        });
        await loaded.Task.WaitAsync(Ct);
        var winner = await WriteAsync(options, profileId, revision, Create("alpha", "winner"));
        release.SetResult();
        return (winner, await loserTask);
    }

    private static Func<LegendaryEventPlanMutationContext, CancellationToken, Task<LegendaryEventPlanMutation>> Create(
        string laneId, params string[] members) =>
        (context, _) =>
        {
            LegendaryEventTeamMutations.CreateTeam(context, laneId, Fields(members));
            return Task.FromResult(LegendaryEventPlanMutation.Applied);
        };

    private static LegendaryEventTeamFields Fields(params string[] members) =>
        new("Team", members, null, [0], 1, 3, "manual");

    private static LegendaryEventPlanResponse Saved(LegendaryEventPlanWriteResult result) =>
        Assert.IsType<LegendaryEventPlanWriteResult.Saved>(result).Plan;

    private static async Task<LegendaryEventPlanWriteResult> WriteAsync(
        DbContextOptions<PlannerDbContext> options,
        ProfileId profileId,
        long expectedRevision,
        Func<LegendaryEventPlanMutationContext, CancellationToken, Task<LegendaryEventPlanMutation>> mutate)
    {
        await using var db = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(profileId));
        return await new LegendaryEventPlanWriter(db, TimeProvider.System).WriteAsync(profileId, EventId, expectedRevision, mutate, Ct);
    }

    private static async Task ExecuteAsync(string connectionString, string sql, params (string Name, Guid Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<long> CountAsync(string connectionString, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {table}";
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static async Task<(DbContextOptions<PlannerDbContext> Options, ProfileId ProfileId)> MigrateAndSeedProfileAsync(
        string connectionString)
    {
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new EntityMetadataInterceptor(TimeProvider.System))
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using (var migrationDb = new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile()))
        {
            await migrationDb.Database.GetService<IMigrator>().MigrateAsync(cancellationToken: Ct);
        }

        var profileId = ProfileId.From(Guid.NewGuid());
        await ExecuteAsync(connectionString, """
            INSERT INTO accounts (id, issuer, subject, created_at, updated_at)
            VALUES (gen_random_uuid(), 'test', 'legendary-event-plans', now(), now());
            INSERT INTO profiles (id, account_id, display_name, created_at, updated_at)
            SELECT @profile, id, 'LRE plans', now(), now() FROM accounts LIMIT 1;
            """, ("profile", profileId.Value));
        return (options, profileId);
    }

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
        public ProfileId? ProfileId { get; } = profileId;
    }
}
