using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence.Encryption;
using Testcontainers.PostgreSql;
using Xunit;

namespace TacticusPlanner.Persistence.IntegrationTests;

/// <summary>
/// Covers the `project-goal-slots` "Concurrent mutations in one project serialize without failing"
/// requirement added by the `fix-goal-mutation-isolation-level` change: concurrent goal mutations
/// against one project must all commit (distinct slots) or fail with the documented structured
/// conflict (same slot) rather than an unhandled `40001` / tracked-entity error. This exercises the
/// real `ExecuteLockedMutationAsync` lock/transaction path, which is a deliberate no-op on the
/// InMemory provider used by the API test suite — hence a Postgres-backed test.
/// </summary>
public sealed class ProjectGoalConcurrencyPostgresTests
{
    [Fact]
    public async Task ConcurrentCreatesForDistinctUnitsAllCommitWithContiguousPriorities()
    {
        await using var postgres = await StartPostgresAsync();
        var (connectionString, profileId, projectId) = await SeedEmptyProjectAsync(postgres);

        var units = new[] { "ragnar", "aunshi", "certus" };
        var results = await Task.WhenAll(units.Select(unitId =>
            CreateGoalAsync(connectionString, profileId, projectId, unitId, GoalType.Rank)));

        Assert.All(results, result => Assert.True(result.Succeeded));
        await AssertContiguousPrioritiesAsync(connectionString, projectId, units.Length);
    }

    [Fact]
    public async Task ConcurrentCreatesForDistinctGoalTypesOnOneUnitAllCommit()
    {
        await using var postgres = await StartPostgresAsync();
        var (connectionString, profileId, projectId) = await SeedEmptyProjectAsync(postgres);

        var results = await Task.WhenAll(
            CreateGoalAsync(connectionString, profileId, projectId, "ragnar", GoalType.Ascension),
            CreateGoalAsync(connectionString, profileId, projectId, "ragnar", GoalType.Rank));

        Assert.All(results, result => Assert.True(result.Succeeded));
        await AssertContiguousPrioritiesAsync(connectionString, projectId, 2);
    }

    [Fact]
    public async Task ConcurrentSameSlotCreatesProduceExactlyOneWinnerAndNoUnhandledError()
    {
        await using var postgres = await StartPostgresAsync();
        var (connectionString, profileId, projectId) = await SeedEmptyProjectAsync(postgres);

        var results = await Task.WhenAll(
            CreateGoalAsync(connectionString, profileId, projectId, "ragnar", GoalType.Rank),
            CreateGoalAsync(connectionString, profileId, projectId, "ragnar", GoalType.Rank));

        Assert.Equal(1, results.Count(result => result.Succeeded));
        Assert.Equal(1, results.Count(result => !result.Succeeded));
        await AssertContiguousPrioritiesAsync(connectionString, projectId, 1);
    }

    [Fact]
    public async Task ConcurrentDistinctRankTargetCreatesBothCommit()
    {
        await using var postgres = await StartPostgresAsync();
        var (connectionString, profileId, projectId) = await SeedEmptyProjectAsync(postgres);

        var results = await Task.WhenAll(
            CreateGoalAsync(connectionString, profileId, projectId, "ragnar", GoalType.Rank, rankEnd: 11),
            CreateGoalAsync(connectionString, profileId, projectId, "ragnar", GoalType.Rank, rankEnd: 12));

        Assert.All(results, result => Assert.True(result.Succeeded));
        await AssertContiguousPrioritiesAsync(connectionString, projectId, 2);
    }

    /// <summary>
    /// Unlike the retired unit-keyed reorder, a goal-order change validates against the project's
    /// *complete* in-flight goal set, not a single slot — it is not slot-scoped (see the corresponding
    /// `project-goal-slots` spec delta). So a goal-order change racing a concurrent create is not
    /// guaranteed to commit the way two distinct-slot creates are: whichever commits second observes
    /// membership the first one already changed, and a goal-order change submitted for the pre-create
    /// set legitimately goes stale and is rejected (not an unhandled error) if the create wins the lock
    /// first. Both outcomes are asserted here rather than a fixed winner, since the actual outcome
    /// depends on which of the two concurrent transactions acquires the project lock first.
    /// </summary>
    [Fact]
    public async Task ConcurrentCreateAndGoalOrderChangeStayConsistentRegardlessOfWhichWins()
    {
        await using var postgres = await StartPostgresAsync();
        var (connectionString, profileId, projectId) = await SeedEmptyProjectAsync(postgres);

        // Seed the two existing goals sequentially (not the behavior under test).
        var ragnarSeed = await CreateGoalAsync(connectionString, profileId, projectId, "ragnar", GoalType.Rank);
        var aunshiSeed = await CreateGoalAsync(connectionString, profileId, projectId, "aunshi", GoalType.Rank);
        Assert.True(ragnarSeed.Succeeded);
        Assert.True(aunshiSeed.Succeeded);

        var createTask = CreateGoalAsync(connectionString, profileId, projectId, "ragnar", GoalType.Ascension);
        var reorderTask = ApplyGoalOrderAsync(connectionString, profileId, projectId,
        [
            aunshiSeed.GoalId,
            ragnarSeed.GoalId,
        ]);

        var createResult = await createTask;
        var reorderSucceeded = await reorderTask;

        // The create is slot-scoped (a new Ascension goal, distinct from the two existing Rank goals'
        // slots) and always commits. The reorder, submitted for the pre-create two-goal set, either
        // wins the lock first and succeeds, or loses it to the create and is correctly rejected as
        // stale — never an unhandled error either way. Either way, occupancy (and so the in-flight
        // count) reflects only the create; a rejected reorder changes nothing.
        Assert.True(createResult.Succeeded);
        _ = reorderSucceeded; // both true and false are valid outcomes here, see the summary above
        await AssertContiguousPrioritiesAsync(connectionString, projectId, 3);
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync()
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        return postgres;
    }

    private static async Task<(string ConnectionString, Guid ProfileId, Guid ProjectId)> SeedEmptyProjectAsync(
        PostgreSqlContainer postgres)
    {
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using var db = new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile());
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);

        var accountId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO accounts (id, issuer, subject, created_at, updated_at)
            VALUES (@account, 'test', 'concurrency', now(), now());
            INSERT INTO profiles (id, account_id, display_name, created_at, updated_at)
            VALUES (@profile, @account, 'Concurrency test', now(), now());
            INSERT INTO projects (id, revision, profile_id, name, status, type, created_at, updated_at)
            VALUES (@project, 0, @profile, 'Concurrency', 'Active', 'Custom', now(), now());
            """;
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.AddWithValue("profile", profileId);
        command.Parameters.AddWithValue("project", projectId);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        return (connectionString, profileId, projectId);
    }

    /// <summary>Mirrors the create-goal path in <c>CreateGoalEndpoint</c>: check the slot inside the lock,
    /// insert the goal + membership, normalize, commit. Returns <c>(false, default)</c> (not an exception)
    /// on a detected slot conflict, matching the endpoint's structured-409 handling.</summary>
    private static async Task<(bool Succeeded, Guid GoalId)> CreateGoalAsync(
        string connectionString, Guid profileId, Guid projectId, string entityId, GoalType goalType,
        int rankEnd = 12)
    {
        var options = BuildOptions(connectionString);
        await using var db = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(ProfileId.From(profileId)));
        var planning = new ProjectGoalPlanningService(db);
        var ct = TestContext.Current.CancellationToken;
        var succeeded = false;
        var createdGoalId = Guid.Empty;
        // A Rank goal always carries a target in the real API (validated on creation), so its membership
        // gets a normalized Rank key; other goal types leave the config empty.
        var config = goalType == GoalType.Rank
            ? new GoalConfig { Rank = new RankTarget { Start = 1, End = rankEnd } }
            : new GoalConfig();
        var rankTargetKey = RankTargetKey.For(goalType, config);

        await planning.ExecuteLockedMutationAsync([ProjectId.From(projectId)], async transaction =>
        {
            if (await planning.FindConflictAsync(
                [ProjectId.From(projectId)], GoalEntityType.Character, entityId, goalType, rankTargetKey, null, ct) is not null)
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(ct);
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var goal = new Goal(GoalEntityType.Character, entityId, goalType)
            {
                Id = GoalId.From(Guid.CreateVersion7()),
                ProfileId = ProfileId.From(profileId),
                Status = GoalStatus.Active,
                Config = config,
                Events = [new GoalEvent { At = now, Type = GoalEventType.Created }],
            };
            db.Goals.Add(goal);

            var project = await db.Projects.FirstAsync(entity => entity.Id == ProjectId.From(projectId), ct);
            var nextPriority = await db.ProjectGoals
                .Where(entity => entity.ProjectId == project.Id)
                .Select(entity => (int?)entity.Priority)
                .MaxAsync(ct) ?? 0;
            db.ProjectGoals.Add(ProjectGoalPlanningService.CreateMembership(project, goal, nextPriority + 1, now));

            await db.SaveChangesAsync(ct);
            await planning.NormalizeAsync([project.Id], ct);
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
            succeeded = true;
            createdGoalId = goal.Id.Value;
        }, ct);

        return (succeeded, createdGoalId);
    }

    private static async Task<bool> ApplyGoalOrderAsync(
        string connectionString, Guid profileId, Guid projectId, List<Guid> goalIds)
    {
        var options = BuildOptions(connectionString);
        await using var db = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(ProfileId.From(profileId)));
        var planning = new ProjectGoalPlanningService(db);
        var ct = TestContext.Current.CancellationToken;
        var applied = false;

        await planning.ExecuteLockedMutationAsync([ProjectId.From(projectId)], async transaction =>
        {
            applied = await planning.ApplyGoalOrderAsync(
                ProjectId.From(projectId), goalIds.Select(GoalId.From).ToList(), ct);
            if (!applied)
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(ct);
                return;
            }

            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }, ct);

        return applied;
    }

    private static async Task AssertContiguousPrioritiesAsync(string connectionString, Guid projectId, int expectedCount)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT priority FROM project_goals WHERE project_id = @project ORDER BY priority;";
        command.Parameters.AddWithValue("project", projectId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var priorities = new List<int>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            priorities.Add(reader.GetInt32(0));

        Assert.Equal(expectedCount, priorities.Count);
        Assert.Equal(priorities.Distinct().Count(), priorities.Count);
        Assert.Equal(Enumerable.Range(1, expectedCount), priorities);
    }

    private static DbContextOptions<PlannerDbContext> BuildOptions(string connectionString) =>
        new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(connectionString, options => options.EnableRetryOnFailure())
            .UseSnakeCaseNamingConvention()
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
        public ProfileId? ProfileId { get; } = profileId;
    }
}
