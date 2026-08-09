using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Encryption;
using Testcontainers.PostgreSql;

namespace TacticusPlanner.Api.Tests;

public sealed class ProjectGoalPostgresTests
{
    [Fact]
    public async Task MigrationBackfillsOrdinaryGoalsDeletesEquipmentAndEnforcesRacingSlots()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);

        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            // This directly constructed test context uses passthrough encryption/profile services rather
            // than the application's design-time services; that changes captured converter/filter
            // instances but not the relational schema under test.
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using var db = new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile());
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260729170305_InitialCreate", TestContext.Current.CancellationToken);

        var accountId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var firstProjectId = Guid.NewGuid();
        var raceProjectId = Guid.NewGuid();
        var ordinaryGoalId = Guid.NewGuid();
        var equipmentGoalId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO accounts (id, issuer, subject, created_at, updated_at)
                VALUES (@account, 'test', 'migration', now(), now());
                INSERT INTO profiles (id, account_id, display_name, created_at, updated_at)
                VALUES (@profile, @account, 'Migration test', now(), now());
                INSERT INTO projects (id, revision, profile_id, name, status, type, created_at, updated_at)
                VALUES (@project, 0, @profile, 'Ordinary', 'Active', 'User', now(), now()),
                       (@raceProject, 0, @profile, 'Race', 'Active', 'User', now(), now());
                INSERT INTO goals (id, revision, profile_id, entity_type, entity_id, goal_type, status,
                                   depends_on, created_at, updated_at, config, events)
                VALUES (@ordinary, 0, @profile, 'Character', 'ragnar', 'Rank', 'Active',
                        ARRAY[]::uuid[], now(), now(), '{}', '[]'),
                       (@equipment, 0, @profile, 'Item', 'equipment-1', 'UpgradeItem', 'Paused',
                        ARRAY[]::uuid[], now(), now(), '{}', '[]');
                INSERT INTO project_goals (project_id, goal_id, priority, created_at)
                VALUES (@project, @ordinary, 1, now()), (@project, @equipment, 2, now());
                """;
            command.Parameters.AddWithValue("account", accountId);
            command.Parameters.AddWithValue("profile", profileId);
            command.Parameters.AddWithValue("project", firstProjectId);
            command.Parameters.AddWithValue("raceProject", raceProjectId);
            command.Parameters.AddWithValue("ordinary", ordinaryGoalId);
            command.Parameters.AddWithValue("equipment", equipmentGoalId);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await migrator.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);

        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var verify = connection.CreateCommand();
            verify.CommandText = """
                SELECT entity_type, entity_id, goal_type, occupies_in_flight_slot
                FROM project_goals WHERE goal_id = @ordinary;
                """;
            verify.Parameters.AddWithValue("ordinary", ordinaryGoalId);
            await using var reader = await verify.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal("Character", reader.GetString(0));
            Assert.Equal("ragnar", reader.GetString(1));
            Assert.Equal("Rank", reader.GetString(2));
            Assert.True(reader.GetBoolean(3));
            Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
        }

        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var verify = connection.CreateCommand();
            verify.CommandText = "SELECT count(*) FROM goals WHERE id = @equipment;";
            verify.Parameters.AddWithValue("equipment", equipmentGoalId);
            Assert.Equal(0L, await verify.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        }

        var racingGoalIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await SeedRacingGoalsAsync(postgres.GetConnectionString(), profileId, racingGoalIds);
        var results = await Task.WhenAll(racingGoalIds.Select(goalId =>
            TryInsertSlotAsync(postgres.GetConnectionString(), raceProjectId, goalId)));
        Assert.Equal(1, results.Count(result => result));
        Assert.Equal(1, results.Count(result => !result));
    }

    private static async Task SeedRacingGoalsAsync(string connectionString, Guid profileId, IEnumerable<Guid> goalIds)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        foreach (var goalId in goalIds)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO goals (id, revision, profile_id, entity_type, entity_id, goal_type, status,
                                   depends_on, created_at, updated_at, config, events)
                VALUES (@goal, 0, @profile, 'Mow', 'forgefiend', 'Ability', 'Active',
                        ARRAY[]::uuid[], now(), now(), '{}', '[]');
                """;
            command.Parameters.AddWithValue("goal", goalId);
            command.Parameters.AddWithValue("profile", profileId);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }

    private static async Task<bool> TryInsertSlotAsync(string connectionString, Guid projectId, Guid goalId)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO project_goals
                    (project_id, goal_id, priority, entity_type, entity_id, goal_type,
                     occupies_in_flight_slot, created_at)
                VALUES (@project, @goal, 1, 'Mow', 'forgefiend', 'Ability', TRUE, now());
                """;
            command.Parameters.AddWithValue("project", projectId);
            command.Parameters.AddWithValue("goal", goalId);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
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
