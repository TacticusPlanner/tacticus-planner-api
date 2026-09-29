using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.Persistence.Interceptors;
using Testcontainers.PostgreSql;
using Xunit;

namespace TacticusPlanner.Persistence.IntegrationTests;

/// <summary>
/// Real-lock coverage for <c>goal-combined-edit</c> (openspec change goals-edit-dialog): the combined edit's
/// project locks, restart-on-membership-drift loop and single transaction only exist on the relational
/// provider (<c>ExecuteLockedMutationAsync</c> is a no-op on InMemory), so they are exercised here with the
/// real <see cref="GoalCombinedEditor"/> resolved from DI, one scope per simulated request.
/// </summary>
public sealed class GoalCombinedEditPostgresTests
{
    [Fact]
    public async Task OverlappingEditsOfGoalsSharingTwoProjectsBothCommitWithoutADeadlock()
    {
        await using var postgres = await StartPostgresAsync();
        var (services, profileId) = await SeedAsync(postgres.GetConnectionString());
        var (p1, p2) = await CreateProjectsAsync(services, profileId);
        var g1 = await CreateGoalAsync(services, profileId, "blackTerminator", [p1, p2]);
        var g2 = await CreateGoalAsync(services, profileId, "deathBlightlord", [p1, p2]);
        var g3 = await CreateGoalAsync(services, profileId, "eldarFarseer", [p1]);
        var order = await ReadOrderRevisionAsync(services, profileId);
        var revision1 = await RevisionAsync(services, g1);

        // Opposite project orders and overlapping lock sets; the shared edit-order (profile row, then
        // projects ascending) must keep them from deadlocking.
        using var start = new ManualResetEventSlim(false);
        var first = Task.Run(() => EditAsync(services, profileId, g1, start, new EditGoalRequest(
            Target: new UpdateGoalTargetRequest(revision1, new GoalTargetEditRequest(Rank: new RankEndTargetRequest(8, false, 0))),
            ProjectIds: [p2.Value, p1.Value],
            Priority: new EditGoalPriorityRequest(3, order))));
        var second = Task.Run(() => EditAsync(services, profileId, g2, start, new EditGoalRequest(
            Details: new UpdateGoalRequest("second", null),
            ProjectIds: [p1.Value, p2.Value])));
        start.Set();
        var outcomes = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        Assert.All(outcomes, outcome => Assert.IsType<GoalEditResult.Applied>(outcome.Result));
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var goals = await db.Goals.ToDictionaryAsync(goal => goal.Id, TestContext.Current.CancellationToken);
        Assert.Equal(8, goals[g1].Config.Rank!.End);
        // No target section: an order move elsewhere bumps this goal's revision, which only a target edit checks.
        Assert.Equal(5, goals[g2].Config.Rank!.End);
        Assert.Equal("second", goals[g2].Notes);
        Assert.Equal(
            [1, 2, 3],
            goals.Values.Select(goal => goal.GlobalPriority!.Value).Order());
        Assert.Equal(3, goals[g1].GlobalPriority);
        Assert.Equal(order + 1, await ReadOrderRevisionAsync(services, profileId));
        var keys = await db.ProjectGoals.Where(entry => entry.GoalId == g1).Select(entry => entry.RankTargetKey)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, keys.Count);
        Assert.All(keys, key => Assert.Equal(RankTargetKey.For(GoalType.Rank, goals[g1].Config), key));
        _ = g3;
    }

    [Fact]
    public async Task AMembershipAddedWhileTheEditWaitsForItsLocksIsValidatedAfterARestart()
    {
        await using var postgres = await StartPostgresAsync();
        var connectionString = postgres.GetConnectionString();
        var (services, profileId) = await SeedAsync(connectionString);
        var (p1, p2) = await CreateProjectsAsync(services, profileId);
        var goal = await CreateGoalAsync(services, profileId, "blackTerminator", [p1]);
        // Another Rank goal for the same unit already holds target 8 in P2.
        var occupant = await CreateGoalAsync(services, profileId, "blackTerminator", [p2], rankEnd: 8);

        // The "other request": holds the profile lock and, once the edit is blocked behind it, adds the goal
        // to P2 and commits.
        await using var blocker = new NpgsqlConnection(connectionString);
        await blocker.OpenAsync(TestContext.Current.CancellationToken);
        await using var blockerTransaction = await blocker.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(blocker, "SELECT id FROM profiles WHERE id = @profile FOR NO KEY UPDATE",
            ("profile", profileId.Value));

        var revision = await RevisionAsync(services, goal);

        // Target only: the edit pre-reads memberships {P1}, so it locks {P1} and then waits on the profile row.
        var edit = Task.Run(() => EditAsync(services, profileId, goal, null, new EditGoalRequest(
            Target: new UpdateGoalTargetRequest(revision, new GoalTargetEditRequest(Rank: new RankEndTargetRequest(8, false, 0))))));
        await WaitForABlockedBackendAsync(connectionString);

        await ExecuteAsync(blocker, """
            INSERT INTO project_goals (project_id, goal_id, entity_type, entity_id, goal_type,
                                       occupies_in_flight_slot, rank_target_key, created_at)
            SELECT @project, id, 'Character', 'ragnar', 'Rank', true, @key, now() FROM goals WHERE id = @goal
            """,
            ("project", p2.Value), ("goal", goal.Value), ("key", RankTargetKey.For(GoalType.Rank, RankConfig(5))!));
        await blockerTransaction.CommitAsync(TestContext.Current.CancellationToken);

        var outcome = await edit.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        // Only the reloaded membership (P1 and P2) sees P2's occupant of target 8.
        var conflict = Assert.IsType<GoalEditResult.SlotConflict>(outcome.Result);
        Assert.Equal(p2.Value, conflict.Body.ProjectId);
        Assert.Equal(occupant.Value, conflict.Body.ExistingGoalId);
        await using var scope = services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<PlannerDbContext>().Goals
            .SingleAsync(entity => entity.Id == goal, TestContext.Current.CancellationToken);
        Assert.Equal(5, stored.Config.Rank!.End);
    }

    private static async Task<GoalEditOutcome> EditAsync(
        ServiceProvider services, ProfileId profileId, GoalId goalId, ManualResetEventSlim? start, EditGoalRequest request)
    {
        await using var scope = services.CreateAsyncScope();
        start?.Wait(TestContext.Current.CancellationToken);
        return await scope.ServiceProvider.GetRequiredService<GoalCombinedEditor>()
            .ApplyAsync(profileId, goalId, request, TestContext.Current.CancellationToken);
    }

    private static async Task WaitForABlockedBackendAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        for (var attempt = 0; attempt < 200; attempt++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND datname = current_database()";
            if ((long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))! > 0)
                return;
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Fail("The edit never blocked on the profile lock.");
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static readonly string[] ProjectNames = ["One", "Two"];

    private static GoalConfig RankConfig(int end) => new() { Rank = new RankTarget { Start = 1, End = end } };

    private static async Task<(ProjectId P1, ProjectId P2)> CreateProjectsAsync(ServiceProvider services, ProfileId profileId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var projects = ProjectNames.Select(name => new Project
        {
            Id = ProjectId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            Name = name,
            Status = ProjectStatus.Active,
            Type = ProjectType.Custom,
        }).ToList();
        db.Projects.AddRange(projects);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (projects[0].Id, projects[1].Id);
    }

    private static async Task<GoalId> CreateGoalAsync(
        ServiceProvider services, ProfileId profileId, string entityId, ProjectId[] projectIds, int rankEnd = 5)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var now = DateTimeOffset.UtcNow;
        var goal = new Goal(GoalEntityType.Character, entityId, GoalType.Rank)
        {
            Id = GoalId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            Status = GoalStatus.Active,
            Config = RankConfig(rankEnd),
            Events = [new GoalEvent { At = now, Type = GoalEventType.Created }],
        };
        db.Goals.Add(goal);
        var order = scope.ServiceProvider.GetRequiredService<GoalOrderService>();
        await order.AppendAsync(goal, ct);
        foreach (var projectId in projectIds)
        {
            var project = await db.Projects.SingleAsync(entity => entity.Id == projectId, ct);
            db.ProjectGoals.Add(ProjectGoalPlanningService.CreateMembership(project, goal, now));
        }

        await order.CompleteAsync(ct);
        return goal.Id;
    }

    private static async Task<long> RevisionAsync(ServiceProvider services, GoalId goalId)
    {
        await using var scope = services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<PlannerDbContext>().Goals
            .AsNoTracking().SingleAsync(goal => goal.Id == goalId, TestContext.Current.CancellationToken)).Revision;
    }

    private static async Task<long> ReadOrderRevisionAsync(ServiceProvider services, ProfileId profileId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        return (await db.Profiles.AsNoTracking().SingleAsync(profile => profile.Id == profileId, TestContext.Current.CancellationToken))
            .GoalOrderRevision;
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync()
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        return postgres;
    }

    /// <summary>Migrates the database, seeds one profile and returns a container whose scopes model requests
    /// of that profile (retrying Npgsql, the interceptor that bumps revisions, and the real goals services).</summary>
    private static async Task<(ServiceProvider Services, ProfileId ProfileId)> SeedAsync(string connectionString)
    {
        var ct = TestContext.Current.CancellationToken;
        var profileId = ProfileId.From(Guid.NewGuid());
        var services = new ServiceCollection()
            .AddGameCatalog()
            .AddSingleton<IColumnEncryptionService, PassthroughEncryption>()
            .AddSingleton<ICurrentProfileProvider>(new StaticProfile(profileId))
            .AddScoped<ProjectGoalPlanningService>()
            .AddGoalsFeature()
            .AddDbContext<PlannerDbContext>(options => options
                .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure())
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(new EntityMetadataInterceptor(TimeProvider.System))
                .ConfigureWarnings(warnings => warnings.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)))
            .BuildServiceProvider();

        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PlannerDbContext>().Database.GetService<IMigrator>()
                .MigrateAsync(cancellationToken: ct);
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await ExecuteAsync(connection, """
            INSERT INTO accounts (id, issuer, subject, created_at, updated_at)
            VALUES (gen_random_uuid(), 'test', 'combined-edit', now(), now());
            INSERT INTO profiles (id, account_id, display_name, created_at, updated_at)
            SELECT @profile, id, 'Combined edit', now(), now() FROM accounts LIMIT 1;
            """, ("profile", profileId.Value));
        return (services, profileId);
    }

    private sealed class PassthroughEncryption : IColumnEncryptionService
    {
        public string? Encrypt(string? plaintext) => plaintext;

        public string? Decrypt(string? envelope) => envelope;
    }

    private sealed class StaticProfile(ProfileId profileId) : ICurrentProfileProvider
    {
        public ProfileId? ProfileId { get; } = profileId;
    }
}
