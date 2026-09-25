using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.Persistence.Interceptors;
using Testcontainers.PostgreSql;
using Xunit;

namespace TacticusPlanner.Persistence.IntegrationTests;

/// <summary>
/// Relational coverage for <c>edit-goal-targets-in-place</c>: the <c>TargetChanged</c> event payload lives
/// in the existing <c>events</c> jsonb column (no schema change), goals written before target editing still
/// read, and a target-only edit is a real revision for optimistic concurrency.
/// </summary>
public sealed class GoalTargetEditPostgresTests
{
    [Fact]
    public async Task OldEventRowsStillReadAndTargetChangedEventsRoundTrip()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var (options, profileId) = await MigrateAndSeedProfileAsync(postgres.GetConnectionString());
        var oldGoalId = Guid.NewGuid();

        // A goal exactly as it was persisted before target editing: events carry only At/Type.
        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO goals (id, revision, profile_id, entity_type, entity_id, goal_type, status,
                                   depends_on, created_at, updated_at, config, events)
                VALUES (@goal, 3, @profile, 'Character', 'ragnar', 'Rank', 'Active', ARRAY[]::uuid[], now(), now(),
                        '{"Rank":{"Start":1,"End":5,"EndPointFive":false,"EndAppliedUpgrades":0}}',
                        '[{"At":"2026-01-01T00:00:00+00:00","Type":"Created"},
                          {"At":"2026-01-02T00:00:00+00:00","Type":"Paused"}]');
                """;
            command.Parameters.AddWithValue("goal", oldGoalId);
            command.Parameters.AddWithValue("profile", profileId.Value);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(profileId)))
        {
            var goal = await db.Goals.SingleAsync(entity => entity.Id == GoalId.From(oldGoalId), TestContext.Current.CancellationToken);
            Assert.Equal([GoalEventType.Created, GoalEventType.Paused], goal.Events.Select(entry => entry.Type));
            Assert.All(goal.Events, entry =>
            {
                Assert.Null(entry.PreviousTarget);
                Assert.Null(entry.NewTarget);
            });

            goal.Config.Rank!.End = 7;
            goal.Events.Add(new GoalEvent
            {
                At = DateTimeOffset.UtcNow,
                Type = GoalEventType.TargetChanged,
                PreviousTarget = new GoalTargetSnapshot { RankEnd = 5, RankEndPointFive = false, RankEndAppliedUpgrades = 0 },
                NewTarget = new GoalTargetSnapshot { RankEnd = 7, RankEndPointFive = true, RankEndAppliedUpgrades = 0 },
            });
            db.Entry(goal).Property(entity => entity.UpdatedAt).IsModified = true;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(profileId)))
        {
            var goal = await db.Goals.SingleAsync(entity => entity.Id == GoalId.From(oldGoalId), TestContext.Current.CancellationToken);
            Assert.Equal(4, goal.Revision); // a target-only edit is a real revision
            Assert.Equal(7, goal.Config.Rank!.End);
            var changed = Assert.Single(goal.Events, entry => entry.Type == GoalEventType.TargetChanged);
            Assert.Equal(5, changed.PreviousTarget!.RankEnd);
            Assert.Equal(7, changed.NewTarget!.RankEnd);
            Assert.True(changed.NewTarget.RankEndPointFive);
            Assert.Equal(3, goal.Events.Count);
        }
    }

    [Fact]
    public async Task UpgradeTargetsInAnEventPayloadRoundTrip()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var (options, profileId) = await MigrateAndSeedProfileAsync(postgres.GetConnectionString());
        var goalId = GoalId.From(Guid.NewGuid());

        await using (var db = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(profileId)))
        {
            db.Goals.Add(new Goal(GoalEntityType.Character, "ragnar", GoalType.Upgrade)
            {
                Id = goalId,
                ProfileId = profileId,
                Status = GoalStatus.Active,
                Config = new GoalConfig
                {
                    Upgrade = new UpgradeTarget { Targets = [new UpgradeMaterialTarget { UpgradeId = "upgHpC014", Quantity = 9 }] },
                },
                Events =
                [
                    new GoalEvent
                    {
                        At = DateTimeOffset.UtcNow,
                        Type = GoalEventType.TargetChanged,
                        PreviousTarget = new GoalTargetSnapshot
                        {
                            UpgradeTargets = [new UpgradeMaterialTarget { UpgradeId = "upgHpC014", Quantity = 2 }],
                        },
                        NewTarget = new GoalTargetSnapshot
                        {
                            UpgradeTargets = [new UpgradeMaterialTarget { UpgradeId = "upgHpC014", Quantity = 9 }],
                        },
                    },
                ],
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(profileId));
        var goal = await verify.Goals.SingleAsync(entity => entity.Id == goalId, TestContext.Current.CancellationToken);
        var changed = Assert.Single(goal.Events);
        Assert.Equal(2, Assert.Single(changed.PreviousTarget!.UpgradeTargets!).Quantity);
        Assert.Equal(9, Assert.Single(changed.NewTarget!.UpgradeTargets!).Quantity);
        Assert.Equal(9, Assert.Single(goal.Config.Upgrade!.Targets).Quantity);
    }

    [Fact]
    public async Task TwoEditsFromTheSameRevisionCannotBothCommit()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var (options, profileId) = await MigrateAndSeedProfileAsync(postgres.GetConnectionString());
        var goalId = GoalId.From(Guid.NewGuid());
        await using (var seed = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(profileId)))
        {
            seed.Goals.Add(new Goal(GoalEntityType.Character, "ragnar", GoalType.Rank)
            {
                Id = goalId,
                ProfileId = profileId,
                Status = GoalStatus.Active,
                Config = new GoalConfig { Rank = new RankTarget { Start = 1, End = 5 } },
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var first = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(profileId));
        await using var second = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(profileId));
        var firstGoal = await first.Goals.SingleAsync(entity => entity.Id == goalId, TestContext.Current.CancellationToken);
        var secondGoal = await second.Goals.SingleAsync(entity => entity.Id == goalId, TestContext.Current.CancellationToken);

        firstGoal.Config.Rank!.End = 7;
        first.Entry(firstGoal).Property(entity => entity.UpdatedAt).IsModified = true;
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);

        secondGoal.Config.Rank!.End = 6;
        second.Entry(secondGoal).Property(entity => entity.UpdatedAt).IsModified = true;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => second.SaveChangesAsync(TestContext.Current.CancellationToken));

        await using var verify = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(profileId));
        Assert.Equal(7, (await verify.Goals.SingleAsync(entity => entity.Id == goalId, TestContext.Current.CancellationToken)).Config.Rank!.End);
    }

    private static async Task<(DbContextOptions<PlannerDbContext> Options, ProfileId ProfileId)> MigrateAndSeedProfileAsync(
        string connectionString)
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            // Revision/UpdatedAt bumps come from this interceptor (registered in Program.cs in the app).
            .AddInterceptors(new EntityMetadataInterceptor(TimeProvider.System))
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using (var migrationDb = new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile()))
        {
            await migrationDb.Database.GetService<IMigrator>().MigrateAsync(cancellationToken: ct);
        }

        var profileId = ProfileId.From(Guid.NewGuid());
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO accounts (id, issuer, subject, created_at, updated_at)
            VALUES (gen_random_uuid(), 'test', 'goal-target-edit', now(), now());
            INSERT INTO profiles (id, account_id, display_name, created_at, updated_at)
            SELECT @profile, id, 'Target edit', now(), now() FROM accounts LIMIT 1;
            """;
        command.Parameters.AddWithValue("profile", profileId.Value);
        await command.ExecuteNonQueryAsync(ct);
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
