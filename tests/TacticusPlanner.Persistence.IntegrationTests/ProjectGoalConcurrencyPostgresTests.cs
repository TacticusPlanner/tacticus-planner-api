using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TacticusPlanner.Api.Features.Goals;
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
    public async Task ConcurrentCreatesForDistinctUnitsAllCommitWithContiguousGlobalPositions()
    {
        await using var postgres = await StartPostgresAsync();
        var (connectionString, profileId, projectId) = await SeedEmptyProjectAsync(postgres);

        var units = new[] { "ragnar", "aunshi", "certus" };
        var results = await Task.WhenAll(units.Select(unitId =>
            CreateGoalAsync(connectionString, profileId, projectId, unitId, GoalType.Rank)));

        Assert.All(results, result => Assert.True(result.Succeeded));
        await AssertContiguousOrderAsync(connectionString, profileId, units.Length);
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
        await AssertContiguousOrderAsync(connectionString, profileId, 2);
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
        await AssertContiguousOrderAsync(connectionString, profileId, 1);
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
        await AssertContiguousOrderAsync(connectionString, profileId, 2);
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
        var loadedRevision = await ReadOrderRevisionAsync(connectionString, profileId);
        var reorderTask = ApplyGoalOrderAsync(connectionString, profileId, projectId,
        [
            aunshiSeed.GoalId,
            ragnarSeed.GoalId,
        ], loadedRevision);

        var createResult = await createTask;
        var reorderSucceeded = await reorderTask;

        // The create is slot-scoped (a new Ascension goal, distinct from the two existing Rank goals'
        // slots) and always commits. The reorder, submitted for the pre-create two-goal set, either
        // wins the lock first and succeeds, or loses it to the create and is correctly rejected as
        // stale — never an unhandled error either way. Either way, occupancy (and so the in-flight
        // count) reflects only the create; a rejected reorder changes nothing.
        Assert.True(createResult.Succeeded);
        _ = reorderSucceeded; // both true and false are valid outcomes here, see the summary above
        await AssertContiguousOrderAsync(connectionString, profileId, 3);
    }

    [Fact]
    public async Task ReorderPermutesPositionsUnderTheDeferredUniqueConstraint()
    {
        await using var postgres = await StartPostgresAsync();
        var (connectionString, profileId, projectId) = await SeedEmptyProjectAsync(postgres);
        var seeds = new List<Guid>();
        foreach (var unit in new[] { "ragnar", "aunshi", "certus" })
            seeds.Add((await CreateGoalAsync(connectionString, profileId, projectId, unit, GoalType.Rank)).GoalId);
        var revision = await ReadOrderRevisionAsync(connectionString, profileId);

        // Reversing swaps positions across rows, which a per-row unique check would reject mid-update.
        var applied = await ApplyGoalOrderAsync(
            connectionString, profileId, projectId, [seeds[2], seeds[1], seeds[0]], revision);

        Assert.True(applied);
        await AssertContiguousOrderAsync(connectionString, profileId, 3);
        Assert.Equal(revision + 1, await ReadOrderRevisionAsync(connectionString, profileId));
    }

    [Fact]
    public async Task ConcurrentReordersWithTheSameRevisionAllowExactlyOneWinner()
    {
        await using var postgres = await StartPostgresAsync();
        var (connectionString, profileId, projectId) = await SeedEmptyProjectAsync(postgres);
        var seeds = new List<Guid>();
        foreach (var unit in new[] { "ragnar", "aunshi", "certus" })
            seeds.Add((await CreateGoalAsync(connectionString, profileId, projectId, unit, GoalType.Rank)).GoalId);
        var revision = await ReadOrderRevisionAsync(connectionString, profileId);

        var results = await Task.WhenAll(
            ApplyGoalOrderAsync(connectionString, profileId, projectId, [seeds[2], seeds[1], seeds[0]], revision),
            ApplyGoalOrderAsync(connectionString, profileId, projectId, [seeds[1], seeds[0], seeds[2]], revision));

        Assert.Equal(1, results.Count(applied => applied));
        await AssertContiguousOrderAsync(connectionString, profileId, 3);
        Assert.Equal(revision + 1, await ReadOrderRevisionAsync(connectionString, profileId));
    }

    [Fact]
    public async Task ConcurrentCompletionAndCreateKeepThePositionsDense()
    {
        await using var postgres = await StartPostgresAsync();
        var (connectionString, profileId, projectId) = await SeedEmptyProjectAsync(postgres);
        var seeds = new List<Guid>();
        foreach (var unit in new[] { "ragnar", "aunshi", "certus" })
            seeds.Add((await CreateGoalAsync(connectionString, profileId, projectId, unit, GoalType.Rank)).GoalId);

        var results = await Task.WhenAll(
            CompleteGoalAsync(connectionString, profileId, seeds[0]),
            CreateGoalAsync(connectionString, profileId, projectId, "gulgortz", GoalType.Rank),
            CreateGoalAsync(connectionString, profileId, projectId, "trajann", GoalType.Rank));

        Assert.True(results[1].Succeeded && results[2].Succeeded);
        await AssertContiguousOrderAsync(connectionString, profileId, 4);
    }

    /// <summary>Mirrors <c>UpdateGoalStatusEndpoint</c>'s completion path: under the lock, take the goal out
    /// of the order, then compact and advance the revision.</summary>
    private static async Task<(bool Succeeded, Guid GoalId)> CompleteGoalAsync(
        string connectionString, Guid profileId, Guid goalId)
    {
        await using var db = new PlannerDbContext(
            BuildOptions(connectionString), new PassthroughEncryption(), new StaticProfile(ProfileId.From(profileId)));
        var planning = new ProjectGoalPlanningService(db);
        var ct = TestContext.Current.CancellationToken;

        await planning.ExecuteLockedMutationAsync([], async transaction =>
        {
            var goal = await db.Goals.FirstAsync(entity => entity.Id == GoalId.From(goalId), ct);
            var order = new GoalOrderService(db);
            goal.Status = GoalStatus.Completed;
            order.Release(goal);
            await order.CompleteAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }, ct);

        return (true, goalId);
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
            var order = new GoalOrderService(db);
            await order.AppendAsync(goal, ct);
            db.ProjectGoals.Add(ProjectGoalPlanningService.CreateMembership(project, goal, now));

            await order.CompleteAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
            succeeded = true;
            createdGoalId = goal.Id.Value;
        }, ct);

        return (succeeded, createdGoalId);
    }

    private static async Task<bool> ApplyGoalOrderAsync(
        string connectionString, Guid profileId, Guid projectId, List<Guid> goalIds, long expectedRevision)
    {
        var options = BuildOptions(connectionString);
        await using var db = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(ProfileId.From(profileId)));
        var planning = new ProjectGoalPlanningService(db);
        var ct = TestContext.Current.CancellationToken;
        var applied = false;

        await planning.ExecuteLockedMutationAsync([ProjectId.From(projectId)], async transaction =>
        {
            var result = await new GoalOrderService(db).ReorderAsync(
                goalIds.Select(GoalId.From).ToList(), expectedRevision, ct);
            applied = result.Outcome == GoalOrderOutcome.Ok;
            if (!applied)
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(ct);
                return;
            }

            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }, ct);

        return applied;
    }

    /// <summary>The account's in-flight goals hold exactly the positions 1..<paramref name="expectedCount"/>.</summary>
    private static async Task AssertContiguousOrderAsync(string connectionString, Guid profileId, int expectedCount)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT global_priority FROM goals WHERE profile_id = @profile AND global_priority IS NOT NULL ORDER BY global_priority;";
        command.Parameters.AddWithValue("profile", profileId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var positions = new List<int>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            positions.Add(reader.GetInt32(0));

        Assert.Equal(Enumerable.Range(1, expectedCount), positions);
    }

    private static async Task<long> ReadOrderRevisionAsync(string connectionString, Guid profileId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT goal_order_revision FROM profiles WHERE id = @profile;";
        command.Parameters.AddWithValue("profile", profileId);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
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
