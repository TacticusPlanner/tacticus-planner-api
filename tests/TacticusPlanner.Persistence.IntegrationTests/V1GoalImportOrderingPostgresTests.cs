using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Api.Features.V1Import;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.Persistence.Encryption;
using Testcontainers.PostgreSql;
using Xunit;

namespace TacticusPlanner.Persistence.IntegrationTests;

/// <summary>
/// Covers `rewrite-v1-goal-import`'s ordering requirement against real PostgreSQL: a multi-unit import
/// yields contiguous in-flight priorities from 1 with no gaps or duplicates, and each unit's goals stay
/// contiguous — the transactional (`ExecuteLockedMutationAsync` + one `NormalizeAsync`) side of the
/// batch that the InMemory-backed API test suite can't exercise (that provider no-ops the lock).
/// </summary>
public sealed class V1GoalImportOrderingPostgresTests
{
    [Fact]
    public async Task MultiUnitImportYieldsContiguousInFlightPrioritiesWithEachUnitsGoalsContiguous()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();

        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        var accountId = Guid.NewGuid();
        var profileId = ProfileId.From(Guid.NewGuid());
        await using (var migrationDb = new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile()))
        {
            await migrationDb.Database.GetService<IMigrator>().MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);
        }

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO accounts (id, issuer, subject, created_at, updated_at)
                VALUES (@account, 'test', 'v1-import-ordering', now(), now());
                INSERT INTO profiles (id, account_id, display_name, created_at, updated_at)
                VALUES (@profile, @account, 'V1 import ordering test', now(), now());
                """;
            command.Parameters.AddWithValue("account", accountId);
            command.Parameters.AddWithValue("profile", profileId.Value);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var services = new ServiceCollection();
        services.AddGameCatalog();
        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IGameCatalogProvider>();

        await using var db = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(profileId));
        // ImportAsync refuses the goals part without a recorded player data snapshot; an empty one (no
        // roster entries) is enough here since this test disables prerequisite synthesis and only cares
        // about ordering, not starting points.
        db.PlayerDataSnapshots.Add(new PlayerDataSnapshot { Id = profileId });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var targetValidation = new GoalTargetValidationService(db, catalog);
        var planning = new ProjectGoalPlanningService(db);
        var projectsService = new ProjectsService(db);
        var importService = new V1GoalImportService(db, catalog, targetValidation, planning, projectsService, TimeProvider.System);

        // Interleaved in V1 priority: blackTerminator (1, 3), ultraInceptorSgt (2) — blackTerminator's
        // block must come first (its lowest priority is 1) and stay contiguous despite the interleaving.
        var goals = new[]
        {
            new V1Goal("bt-rank", "blackTerminator", 1, 1, true, null, null, null, null, 4, false, 0,
                null, null, null, null, null, null, null),
            new V1Goal("ci-rank", "ultraInceptorSgt", 1, 2, true, null, null, null, null, 4, false, 0,
                null, null, null, null, null, null, null),
            new V1Goal("bt-ascend", "blackTerminator", 2, 3, true, null, null, null, null, null, null, null,
                null, null, 0, 2, null, null, null),
        };

        var result = await importService.ImportAsync(profileId, goals, synthesizePrerequisites: false, TestContext.Current.CancellationToken);
        Assert.All(result.Outcomes, outcome => Assert.Equal("Created", outcome.Status));

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT entity_id, priority FROM project_goals
                WHERE occupies_in_flight_slot ORDER BY priority;
                """;
            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            var rows = new List<(string EntityId, int Priority)>();
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                rows.Add((reader.GetString(0), reader.GetInt32(1)));
            }

            Assert.Equal(3, rows.Count);
            var priorities = rows.Select(row => row.Priority).ToList();
            Assert.Equal(priorities.Distinct().Count(), priorities.Count);
            Assert.Equal([1, 2, 3], priorities.OrderBy(value => value).ToList());

            // blackTerminator's block (2 goals) is contiguous and comes before ultraInceptorSgt's.
            var blackTerminatorPriorities = rows.Where(row => row.EntityId == "blackTerminator")
                .Select(row => row.Priority).OrderBy(value => value).ToList();
            var ultraInceptorPriority = rows.Single(row => row.EntityId == "ultraInceptorSgt").Priority;
            Assert.Equal([1, 2], blackTerminatorPriorities);
            Assert.Equal(3, ultraInceptorPriority);
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

    private sealed class StaticProfile(ProfileId profileId) : ICurrentProfileProvider
    {
        public ProfileId? ProfileId { get; } = profileId;
    }
}
