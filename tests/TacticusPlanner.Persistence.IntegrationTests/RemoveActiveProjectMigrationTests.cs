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
/// Covers the <c>RemoveActiveProject</c> migration (openspec change
/// consolidate-goals-into-plan-and-remove-active-project): every profile ends with exactly one Default
/// project (missing ones are created, extra ones demoted to Custom, oldest kept), goals and memberships
/// are untouched, <c>profiles.active_project_id</c> is gone, and the database enforces the invariant.
/// </summary>
public sealed class RemoveActiveProjectMigrationTests
{
    private const string Previous = "20260926063032_AddGlobalGoalPriority";
    private const string EmptyJson = "{}";

    [Fact]
    public async Task MigrationLeavesExactlyOneDefaultProjectPerProfileAndKeepsMemberships()
    {
        await using var postgres = await StartAsync();
        var (migrator, connection) = await OpenAtPreviousAsync(postgres);
        await using var _ = connection;

        // No Default project at all; the (custom) active project holds a goal.
        var noDefault = await SeedProfileAsync(connection, "none");
        var custom = await SeedProjectAsync(connection, noDefault, "Custom", "Custom", "2026-01-01");
        await SetActiveAsync(connection, noDefault, custom);
        var goalOne = await SeedGoalWithMembershipAsync(connection, noDefault, custom, "u1");

        // Two Default rows: the oldest stays Default, the newer becomes Custom, both keep their goals.
        var twoDefaults = await SeedProfileAsync(connection, "two");
        var older = await SeedProjectAsync(connection, twoDefaults, "My Goals", "Default", "2026-01-01");
        var newer = await SeedProjectAsync(connection, twoDefaults, "Duplicate", "Default", "2026-02-01");
        await SetActiveAsync(connection, twoDefaults, newer);
        await SeedGoalWithMembershipAsync(connection, twoDefaults, older, "u2");
        await SeedGoalWithMembershipAsync(connection, twoDefaults, newer, "u3");

        // A healthy profile whose active project is not the default.
        var healthy = await SeedProfileAsync(connection, "ok");
        var healthyDefault = await SeedProjectAsync(connection, healthy, "My Goals", "Default", "2026-01-01");
        var healthyCustom = await SeedProjectAsync(connection, healthy, "Event", "Custom", "2026-01-02");
        await SetActiveAsync(connection, healthy, healthyCustom);
        await SeedGoalWithMembershipAsync(connection, healthy, healthyDefault, "u4");

        await migrator.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1L, await ScalarAsync(connection, DefaultCount(noDefault)));
        Assert.Equal("My Goals", await ScalarAsync(connection,
            $"SELECT name FROM projects WHERE profile_id = '{noDefault}' AND type = 'Default'"));
        Assert.Equal("Custom", await ScalarAsync(connection, $"SELECT type FROM projects WHERE id = '{custom}'"));
        Assert.Equal(1L, await ScalarAsync(connection,
            $"SELECT count(*) FROM project_goals WHERE goal_id = '{goalOne}' AND project_id = '{custom}'"));

        Assert.Equal(1L, await ScalarAsync(connection, DefaultCount(twoDefaults)));
        Assert.Equal("Default", await ScalarAsync(connection, $"SELECT type FROM projects WHERE id = '{older}'"));
        Assert.Equal("Custom", await ScalarAsync(connection, $"SELECT type FROM projects WHERE id = '{newer}'"));
        Assert.Equal(2L, await ScalarAsync(connection,
            $"SELECT count(*) FROM project_goals WHERE project_id IN ('{older}', '{newer}')"));

        Assert.Equal(1L, await ScalarAsync(connection, DefaultCount(healthy)));
        Assert.Equal(2L, await ScalarAsync(connection, $"SELECT count(*) FROM projects WHERE profile_id = '{healthy}'"));
        Assert.Equal(4L, await ScalarAsync(connection, "SELECT count(*) FROM goals"));
        Assert.Equal(4L, await ScalarAsync(connection, "SELECT count(*) FROM project_goals"));

        Assert.Equal(0L, await ScalarAsync(connection,
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'profiles' AND column_name = 'active_project_id'"));
        var second = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, $"""
            INSERT INTO projects (id, revision, profile_id, name, status, type, created_at, updated_at)
            VALUES (gen_random_uuid(), 0, '{healthy}', 'Another', 'Active', 'Default', now(), now());
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, second.SqlState);
    }

    [Fact]
    public async Task DownRestoresTheColumnPointingAtEachDefaultProject()
    {
        await using var postgres = await StartAsync();
        var (migrator, connection) = await OpenAtPreviousAsync(postgres);
        await using var _ = connection;
        var profile = await SeedProfileAsync(connection, "down");
        var defaultProject = await SeedProjectAsync(connection, profile, "My Goals", "Default", "2026-01-01");

        await migrator.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);
        await migrator.MigrateAsync(Previous, TestContext.Current.CancellationToken);

        Assert.Equal(defaultProject, await ScalarAsync(connection, $"SELECT active_project_id FROM profiles WHERE id = '{profile}'"));
        Assert.Equal(0L, await ScalarAsync(connection,
            "SELECT count(*) FROM pg_indexes WHERE indexname = 'ix_projects_profile_id_default'"));
    }

    private static string DefaultCount(Guid profile) =>
        $"SELECT count(*) FROM projects WHERE profile_id = '{profile}' AND type = 'Default'";

    private static async Task<PostgreSqlContainer> StartAsync()
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        return postgres;
    }

    private static async Task<(IMigrator Migrator, NpgsqlConnection Connection)> OpenAtPreviousAsync(
        PostgreSqlContainer postgres)
    {
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(connectionString)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        var db = new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile());
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous, TestContext.Current.CancellationToken);

        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return (migrator, connection);
    }

    private static async Task<Guid> SeedProfileAsync(NpgsqlConnection connection, string name)
    {
        var profile = Guid.NewGuid();
        await ExecuteAsync(connection, $"""
            INSERT INTO accounts (id, issuer, subject, created_at, updated_at)
            VALUES (gen_random_uuid(), 'test', 'remove-active-{name}', now(), now());
            INSERT INTO profiles (id, account_id, display_name, created_at, updated_at)
            SELECT '{profile}', id, 'Profile {name}', now(), now() FROM accounts WHERE subject = 'remove-active-{name}';
            """);
        return profile;
    }

    private static async Task<Guid> SeedProjectAsync(
        NpgsqlConnection connection, Guid profile, string name, string type, string createdAt)
    {
        var project = Guid.NewGuid();
        await ExecuteAsync(connection, $"""
            INSERT INTO projects (id, revision, profile_id, name, status, type, created_at, updated_at)
            VALUES ('{project}', 0, '{profile}', '{name}', 'Active', '{type}', '{createdAt}', '{createdAt}');
            """);
        return project;
    }

    private static Task SetActiveAsync(NpgsqlConnection connection, Guid profile, Guid project) =>
        ExecuteAsync(connection, $"UPDATE profiles SET active_project_id = '{project}' WHERE id = '{profile}'");

    private static async Task<Guid> SeedGoalWithMembershipAsync(
        NpgsqlConnection connection, Guid profile, Guid project, string unit)
    {
        var goal = Guid.NewGuid();
        await ExecuteAsync(connection, $"""
            INSERT INTO goals (id, revision, profile_id, entity_type, entity_id, goal_type, status,
                               depends_on, created_at, updated_at, config, events)
            VALUES ('{goal}', 0, '{profile}', 'Character', '{unit}', 'Unlock', 'Completed',
                    ARRAY[]::uuid[], now(), now(), '{EmptyJson}', '[]');
            INSERT INTO project_goals (project_id, goal_id, entity_type, entity_id, goal_type,
                                       occupies_in_flight_slot, created_at)
            VALUES ('{project}', '{goal}', 'Character', '{unit}', 'Unlock', FALSE, now());
            """);
        return goal;
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
