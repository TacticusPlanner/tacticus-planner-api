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
/// Covers the <c>RemoveLevelGoals</c> migration (openspec change integrate-level-progression-into-rank-goals):
/// existing Level goals and their project memberships are deleted, and every dependency edge pointing at
/// one is removed from the surviving goals, which keep their other dependencies.
/// </summary>
public sealed class RemoveLevelGoalsMigrationTests
{
    [Fact]
    public async Task MigrationDeletesLevelGoalsAndCleansDependencyEdges()
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
        await migrator.MigrateAsync("20260925113856_AddGoalTargetChangedEventPayload", TestContext.Current.CancellationToken);

        var profileId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var levelGoal = Guid.NewGuid();
        var unlockGoal = Guid.NewGuid();
        var rankGoal = Guid.NewGuid();
        var untouchedGoal = Guid.NewGuid();

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO accounts (id, issuer, subject, created_at, updated_at)
                VALUES (gen_random_uuid(), 'test', 'remove-level', now(), now());
                INSERT INTO profiles (id, account_id, display_name, created_at, updated_at)
                SELECT @profile, id, 'Remove level', now(), now() FROM accounts LIMIT 1;
                INSERT INTO projects (id, revision, profile_id, name, status, type, created_at, updated_at)
                VALUES (@project, 0, @profile, 'Plan', 'Active', 'User', now(), now());

                INSERT INTO goals (id, revision, profile_id, entity_type, entity_id, goal_type, status,
                                   depends_on, created_at, updated_at, config, events)
                VALUES
                (@level, 0, @profile, 'Character', 'a', 'Level', 'Active', ARRAY[]::uuid[], now(), now(),
                 '{"Level":{"Start":1,"End":30}}', '[]'),
                (@unlock, 0, @profile, 'Character', 'a', 'Unlock', 'Active', ARRAY[]::uuid[], now(), now(),
                 '{}', '[]'),
                (@rank, 3, @profile, 'Character', 'a', 'Rank', 'Active', ARRAY[@unlock, @level]::uuid[], now(), now(),
                 '{"Rank":{"Start":1,"End":12,"EndPointFive":false,"EndAppliedUpgrades":0}}', '[]'),
                (@untouched, 0, @profile, 'Character', 'b', 'Unlock', 'Active', ARRAY[]::uuid[], now(), now(),
                 '{}', '[]');

                INSERT INTO project_goals (project_id, goal_id, priority, entity_type, entity_id, goal_type,
                                           occupies_in_flight_slot, created_at)
                VALUES
                (@project, @level, 1, 'Character', 'a', 'Level', TRUE, now()),
                (@project, @rank, 2, 'Character', 'a', 'Rank', TRUE, now());
                """;
            command.Parameters.AddWithValue("profile", profileId);
            command.Parameters.AddWithValue("project", projectId);
            command.Parameters.AddWithValue("level", levelGoal);
            command.Parameters.AddWithValue("unlock", unlockGoal);
            command.Parameters.AddWithValue("rank", rankGoal);
            command.Parameters.AddWithValue("untouched", untouchedGoal);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await migrator.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);

        await using var verify = new NpgsqlConnection(connectionString);
        await verify.OpenAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0L, await ScalarAsync(verify, "SELECT count(*) FROM goals WHERE goal_type = 'Level'"));
        Assert.Equal(0L, await ScalarAsync(verify, $"SELECT count(*) FROM project_goals WHERE goal_id = '{levelGoal}'"));
        Assert.Equal(1L, await ScalarAsync(verify, $"SELECT count(*) FROM project_goals WHERE goal_id = '{rankGoal}'"));

        // The Rank goal keeps its Unlock dependency, loses only the Level edge, and has its revision bumped.
        Assert.Equal(new[] { unlockGoal }, (Guid[])(await ScalarAsync(verify, $"SELECT depends_on FROM goals WHERE id = '{rankGoal}'"))!);
        Assert.Equal(4L, await ScalarAsync(verify, $"SELECT revision FROM goals WHERE id = '{rankGoal}'"));
        // A goal that never depended on a Level goal is left exactly as it was.
        Assert.Equal(0L, await ScalarAsync(verify, $"SELECT revision FROM goals WHERE id = '{untouchedGoal}'"));
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
