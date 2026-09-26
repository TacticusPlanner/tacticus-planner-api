using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence.Encryption;
using Testcontainers.PostgreSql;
using Xunit;

namespace TacticusPlanner.Persistence.IntegrationTests;

/// <summary>
/// Covers the <c>AddGlobalGoalPriority</c> migration (openspec change establish-global-goal-priority):
/// each profile's per-project orders become one account-wide order — the former Current plan first, then
/// other projects by creation time, shared goals once, memberless in-flight goals last — while historical
/// goals keep their status and take no position.
/// </summary>
public sealed class AddGlobalGoalPriorityMigrationTests
{
    private const string EmptyJson = "{}";

    [Fact]
    public async Task MigrationMaterializesADeterministicGlobalOrderPerProfile()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();

        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(connectionString)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using var db = new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile());
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260925204338_RemoveLevelGoals", TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        // Profile 1: Current plan A holds X then Y; the older project B holds Y then Z (and a Paused P);
        // W is in-flight with no project; H is Completed. Expected order: X, Y, Z, P, W.
        var profileOne = await SeedProfileAsync(connection, "one");
        var currentPlan = await SeedProjectAsync(connection, profileOne, "A", createdAt: "2026-03-01");
        var olderProject = await SeedProjectAsync(connection, profileOne, "B", createdAt: "2026-01-01");
        await SetCurrentPlanAsync(connection, profileOne, currentPlan);
        var (x, y, z, p, w, h) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await SeedGoalAsync(connection, profileOne, x, "ux", "Active", "2026-02-01");
        await SeedGoalAsync(connection, profileOne, y, "uy", "Active", "2026-02-02");
        await SeedGoalAsync(connection, profileOne, z, "uz", "Active", "2026-02-03");
        await SeedGoalAsync(connection, profileOne, p, "up", "Paused", "2026-02-04");
        await SeedGoalAsync(connection, profileOne, w, "uw", "Active", "2026-02-05");
        await SeedGoalAsync(connection, profileOne, h, "uh", "Completed", "2026-02-06");
        await SeedMembershipAsync(connection, currentPlan, x, "ux", "Active", 1);
        await SeedMembershipAsync(connection, currentPlan, y, "uy", "Active", 2);
        await SeedMembershipAsync(connection, currentPlan, h, "uh", "Completed", 3);
        await SeedMembershipAsync(connection, olderProject, y, "uy", "Active", 1);
        await SeedMembershipAsync(connection, olderProject, z, "uz", "Active", 2);
        await SeedMembershipAsync(connection, olderProject, p, "up", "Paused", 3);

        // Profile 2: no Current plan, so projects contribute in creation order: M, N from the older one,
        // then Q (a Machine of War goal; N is already placed). An empty project changes nothing.
        var profileTwo = await SeedProfileAsync(connection, "two");
        var first = await SeedProjectAsync(connection, profileTwo, "First", createdAt: "2026-01-01");
        var second = await SeedProjectAsync(connection, profileTwo, "Second", createdAt: "2026-02-01");
        _ = await SeedProjectAsync(connection, profileTwo, "Empty", createdAt: "2026-03-01");
        var (m, n, q) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await SeedGoalAsync(connection, profileTwo, q, "uq", "Active", "2026-01-01", entityType: "Mow");
        await SeedGoalAsync(connection, profileTwo, n, "un", "Active", "2026-01-02");
        await SeedGoalAsync(connection, profileTwo, m, "um", "Active", "2026-01-03");
        await SeedMembershipAsync(connection, second, n, "un", "Active", 1);
        await SeedMembershipAsync(connection, second, q, "uq", "Active", 2, entityType: "Mow");
        await SeedMembershipAsync(connection, first, m, "um", "Active", 1);
        await SeedMembershipAsync(connection, first, n, "un", "Active", 2);

        await migrator.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([x, y, z, p, w], await OrderAsync(connection, profileOne));
        Assert.Equal([m, n, q], await OrderAsync(connection, profileTwo));

        // Positions are dense per profile, historical goals hold none and keep their status.
        Assert.Equal([1, 2, 3, 4, 5], await PositionsAsync(connection, profileOne));
        Assert.Equal([1, 2, 3], await PositionsAsync(connection, profileTwo));
        Assert.Equal("Completed", await ScalarAsync(connection, $"SELECT status FROM goals WHERE id = '{h}'"));
        Assert.Equal(DBNull.Value, await ScalarAsync(connection, $"SELECT global_priority FROM goals WHERE id = '{h}'"));
        Assert.Equal(6L, await ScalarAsync(connection, $"SELECT count(*) FROM goals WHERE profile_id = '{profileOne}'"));
        Assert.Equal(0L, await ScalarAsync(connection, $"SELECT goal_order_revision FROM profiles WHERE id = '{profileOne}'"));

        // The old per-project priority is gone and the invariants are enforced by the database.
        Assert.Equal(0L, await ScalarAsync(connection,
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'project_goals' AND column_name = 'priority'"));
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
            $"UPDATE goals SET global_priority = 1 WHERE id = '{y}'"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        var missing = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
            $"UPDATE goals SET global_priority = NULL WHERE id = '{x}'"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, missing.SqlState);
    }

    private static async Task<Guid> SeedProfileAsync(NpgsqlConnection connection, string name)
    {
        var profile = Guid.NewGuid();
        await ExecuteAsync(connection, $"""
            INSERT INTO accounts (id, issuer, subject, created_at, updated_at)
            VALUES (gen_random_uuid(), 'test', 'global-priority-{name}', now(), now());
            INSERT INTO profiles (id, account_id, display_name, created_at, updated_at)
            SELECT '{profile}', id, 'Profile {name}', now(), now() FROM accounts WHERE subject = 'global-priority-{name}';
            """);
        return profile;
    }

    private static async Task<Guid> SeedProjectAsync(NpgsqlConnection connection, Guid profile, string name, string createdAt)
    {
        var project = Guid.NewGuid();
        await ExecuteAsync(connection, $"""
            INSERT INTO projects (id, revision, profile_id, name, status, type, created_at, updated_at)
            VALUES ('{project}', 0, '{profile}', '{name}', 'Active', 'User', '{createdAt}', '{createdAt}');
            """);
        return project;
    }

    private static Task SetCurrentPlanAsync(NpgsqlConnection connection, Guid profile, Guid project) =>
        ExecuteAsync(connection, $"UPDATE profiles SET active_project_id = '{project}' WHERE id = '{profile}'");

    private static Task SeedGoalAsync(
        NpgsqlConnection connection, Guid profile, Guid goal, string unit, string status, string createdAt,
        string entityType = "Character") =>
        ExecuteAsync(connection, $"""
            INSERT INTO goals (id, revision, profile_id, entity_type, entity_id, goal_type, status,
                               depends_on, created_at, updated_at, config, events)
            VALUES ('{goal}', 0, '{profile}', '{entityType}', '{unit}', 'Unlock', '{status}',
                    ARRAY[]::uuid[], '{createdAt}', '{createdAt}', '{EmptyJson}', '[]');
            """);

    private static Task SeedMembershipAsync(
        NpgsqlConnection connection, Guid project, Guid goal, string unit, string status, int priority,
        string entityType = "Character") =>
        ExecuteAsync(connection, $"""
            INSERT INTO project_goals (project_id, goal_id, priority, entity_type, entity_id, goal_type,
                                       occupies_in_flight_slot, created_at)
            VALUES ('{project}', '{goal}', {priority}, '{entityType}', '{unit}', 'Unlock',
                    {(status is "Active" or "Paused" ? "TRUE" : "FALSE")}, now());
            """);

    private static async Task<List<Guid>> OrderAsync(NpgsqlConnection connection, Guid profile)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT id FROM goals WHERE profile_id = '{profile}' AND global_priority IS NOT NULL ORDER BY global_priority";
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var ids = new List<Guid>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            ids.Add(reader.GetGuid(0));
        return ids;
    }

    private static async Task<List<int>> PositionsAsync(NpgsqlConnection connection, Guid profile)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT global_priority FROM goals WHERE profile_id = '{profile}' AND global_priority IS NOT NULL ORDER BY global_priority";
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var positions = new List<int>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            positions.Add(reader.GetInt32(0));
        return positions;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
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
}
