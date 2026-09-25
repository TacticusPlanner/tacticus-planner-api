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
/// Covers the <c>TargetSpecificRankSlots</c> migration (openspec change support-multiple-rank-milestones):
/// existing Rank memberships are backfilled with the same normalized key the C# <c>RankTargetKey</c>
/// produces, and the replacement partial indexes let distinct Rank targets coexist while rejecting an
/// exact duplicate and keeping the non-Rank one-per-unit/type rule.
/// </summary>
public sealed class RankTargetSlotsPostgresTests
{
    [Fact]
    public async Task MigrationBackfillsRankKeysAndIndexesSeparateRankTargets()
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
        await migrator.MigrateAsync("20260911182421_AddGuildRaidStatus", TestContext.Current.CancellationToken);

        var profileId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var cleanGoal = Guid.NewGuid();
        var pointFiveGoal = Guid.NewGuid();
        var threeSlotsGoal = Guid.NewGuid();
        var adamantineGoal = Guid.NewGuid();
        var abilityGoal = Guid.NewGuid();

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO accounts (id, issuer, subject, created_at, updated_at)
                VALUES (gen_random_uuid(), 'test', 'rank-slots', now(), now());
                INSERT INTO profiles (id, account_id, display_name, created_at, updated_at)
                SELECT @profile, id, 'Rank slots', now(), now() FROM accounts LIMIT 1;
                INSERT INTO projects (id, revision, profile_id, name, status, type, created_at, updated_at)
                VALUES (@project, 0, @profile, 'Ranks', 'Active', 'User', now(), now());

                INSERT INTO goals (id, revision, profile_id, entity_type, entity_id, goal_type, status,
                                   depends_on, created_at, updated_at, config, events)
                VALUES
                (@clean, 0, @profile, 'Character', 'a', 'Rank', 'Active', ARRAY[]::uuid[], now(), now(),
                 '{"Rank":{"Start":1,"End":12,"EndPointFive":false,"EndAppliedUpgrades":0}}', '[]'),
                (@pointFive, 0, @profile, 'Character', 'b', 'Rank', 'Active', ARRAY[]::uuid[], now(), now(),
                 '{"Rank":{"Start":1,"End":12,"EndPointFive":true,"EndAppliedUpgrades":0}}', '[]'),
                (@threeSlots, 0, @profile, 'Character', 'c', 'Rank', 'Active', ARRAY[]::uuid[], now(), now(),
                 '{"Rank":{"Start":1,"End":12,"EndPointFive":false,"EndAppliedUpgrades":3}}', '[]'),
                (@adamantine, 0, @profile, 'Character', 'd', 'Rank', 'Active', ARRAY[]::uuid[], now(), now(),
                 '{"Rank":{"Start":1,"End":18,"EndPointFive":true,"EndAppliedUpgrades":2}}', '[]'),
                (@ability, 0, @profile, 'Mow', 'e', 'Ability', 'Active', ARRAY[]::uuid[], now(), now(),
                 '{"Ability":{"ActiveStart":1,"ActiveEnd":2,"PassiveStart":1,"PassiveEnd":2}}', '[]');

                INSERT INTO project_goals (project_id, goal_id, priority, entity_type, entity_id, goal_type,
                                           occupies_in_flight_slot, created_at)
                VALUES
                (@project, @clean, 1, 'Character', 'a', 'Rank', TRUE, now()),
                (@project, @pointFive, 2, 'Character', 'b', 'Rank', TRUE, now()),
                (@project, @threeSlots, 3, 'Character', 'c', 'Rank', TRUE, now()),
                (@project, @adamantine, 4, 'Character', 'd', 'Rank', TRUE, now()),
                (@project, @ability, 5, 'Mow', 'e', 'Ability', TRUE, now());
                """;
            command.Parameters.AddWithValue("profile", profileId);
            command.Parameters.AddWithValue("project", projectId);
            command.Parameters.AddWithValue("clean", cleanGoal);
            command.Parameters.AddWithValue("pointFive", pointFiveGoal);
            command.Parameters.AddWithValue("threeSlots", threeSlotsGoal);
            command.Parameters.AddWithValue("adamantine", adamantineGoal);
            command.Parameters.AddWithValue("ability", abilityGoal);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await migrator.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);

        await using var verify = new NpgsqlConnection(connectionString);
        await verify.OpenAsync(TestContext.Current.CancellationToken);
        // Same rules as RankTargetKey.From: point-five == three applied slots below Adamantine1, and
        // point-five is ignored at Adamantine1+.
        Assert.Equal("12:0", await KeyAsync(verify, cleanGoal));
        Assert.Equal("12:3", await KeyAsync(verify, pointFiveGoal));
        Assert.Equal("12:3", await KeyAsync(verify, threeSlotsGoal));
        Assert.Equal("18:2", await KeyAsync(verify, adamantineGoal));
        Assert.Null(await KeyAsync(verify, abilityGoal));

        // A second in-flight Rank goal with a distinct target for the same unit is accepted; the exact
        // same target is rejected; a duplicate non-Rank slot is still rejected.
        await InsertMembershipAsync(verify, projectId, "a", "Rank", "13:0");
        await Assert.ThrowsAsync<PostgresException>(() => InsertMembershipAsync(verify, projectId, "a", "Rank", "13:0"));
        await Assert.ThrowsAsync<PostgresException>(() => InsertMembershipAsync(verify, projectId, "e", "Ability", null, "Mow"));
    }

    /// <summary>Hard delete (what <c>DeleteGoalEndpoint</c> does) cascades every project membership, so the
    /// Rank target is free again in each project and a recreation is a brand-new goal with only the
    /// explicitly requested memberships. The cascade is a database FK behavior, hence Postgres.</summary>
    [Fact]
    public async Task DeletingARankGoalReleasesItsTargetInEveryProjectAndRecreationIsFresh()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        var ct = TestContext.Current.CancellationToken;

        var profileId = ProfileId.From(Guid.NewGuid());
        var projectIds = new[] { ProjectId.From(Guid.NewGuid()), ProjectId.From(Guid.NewGuid()) };
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using (var migrationDb = new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile()))
        {
            await migrationDb.Database.GetService<IMigrator>().MigrateAsync(cancellationToken: ct);
        }

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO accounts (id, issuer, subject, created_at, updated_at)
                VALUES (gen_random_uuid(), 'test', 'rank-delete', now(), now());
                INSERT INTO profiles (id, account_id, display_name, created_at, updated_at)
                SELECT @profile, id, 'Rank delete', now(), now() FROM accounts LIMIT 1;
                INSERT INTO projects (id, revision, profile_id, name, status, type, created_at, updated_at)
                VALUES (@a, 0, @profile, 'A', 'Active', 'Custom', now(), now()),
                       (@b, 0, @profile, 'B', 'Active', 'Custom', now(), now());
                """;
            command.Parameters.AddWithValue("profile", profileId.Value);
            command.Parameters.AddWithValue("a", projectIds[0].Value);
            command.Parameters.AddWithValue("b", projectIds[1].Value);
            await command.ExecuteNonQueryAsync(ct);
        }

        var original = await AddRankGoalInBothProjectsAsync(options, profileId, projectIds, status: GoalStatus.Paused);

        await using (var db = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(profileId)))
        {
            db.Goals.Remove(await db.Goals.SingleAsync(goal => goal.Id == original, ct));
            await db.SaveChangesAsync(ct);
        }

        // The unique index would reject either recreation if a stale membership still held the target.
        var recreated = await AddRankGoalInBothProjectsAsync(options, profileId, projectIds, status: GoalStatus.Active);

        await using var verify = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(profileId));
        Assert.NotEqual(original, recreated);
        Assert.Equal(0, await verify.ProjectGoals.CountAsync(entry => entry.GoalId == original, ct));
        var memberships = await verify.ProjectGoals.Where(entry => entry.GoalId == recreated).ToListAsync(ct);
        Assert.Equal(2, memberships.Count);
        Assert.All(memberships, entry => Assert.Equal("12:0", entry.RankTargetKey));
        var goal = await verify.Goals.SingleAsync(entry => entry.Id == recreated, ct);
        Assert.Equal(GoalStatus.Active, goal.Status);
        Assert.Equal([GoalEventType.Created], goal.Events.Select(entry => entry.Type));
    }

    /// <summary>The database backstop for a Rank race: when the friendly pre-check is bypassed and two goals
    /// claim the same Rank target in one project, the Rank-target index rejects the second — and that
    /// violation must be recognised as a slot conflict (so endpoints answer 409, not 500), like the
    /// non-Rank index's.</summary>
    [Fact]
    public async Task RankTargetIndexViolationIsRecognisedAsASlotConflict()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        var ct = TestContext.Current.CancellationToken;
        var profileId = ProfileId.From(Guid.NewGuid());
        var projectId = ProjectId.From(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using (var migrationDb = new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile()))
        {
            await migrationDb.Database.GetService<IMigrator>().MigrateAsync(cancellationToken: ct);
        }

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO accounts (id, issuer, subject, created_at, updated_at)
                VALUES (gen_random_uuid(), 'test', 'rank-race', now(), now());
                INSERT INTO profiles (id, account_id, display_name, created_at, updated_at)
                SELECT @profile, id, 'Rank race', now(), now() FROM accounts LIMIT 1;
                INSERT INTO projects (id, revision, profile_id, name, status, type, created_at, updated_at)
                VALUES (@project, 0, @profile, 'A', 'Active', 'Custom', now(), now());
                """;
            command.Parameters.AddWithValue("profile", profileId.Value);
            command.Parameters.AddWithValue("project", projectId.Value);
            await command.ExecuteNonQueryAsync(ct);
        }

        await AddRankGoalInBothProjectsAsync(options, profileId, [projectId], GoalStatus.Active);
        var raced = await Assert.ThrowsAsync<DbUpdateException>(
            () => AddRankGoalInBothProjectsAsync(options, profileId, [projectId], GoalStatus.Active));

        Assert.True(GoalConflictDetection.IsProjectSlotConflict(raced));
        Assert.Equal("ix_project_goals_one_in_flight_rank_target", (raced.InnerException as PostgresException)?.ConstraintName);
    }

    private static async Task<GoalId> AddRankGoalInBothProjectsAsync(
        DbContextOptions<PlannerDbContext> options, ProfileId profileId, ProjectId[] projectIds, GoalStatus status)
    {
        await using var db = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(profileId));
        var now = DateTimeOffset.UtcNow;
        var goal = new Goal(GoalEntityType.Character, "ragnar", GoalType.Rank)
        {
            Id = GoalId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            Status = status,
            Config = new GoalConfig { Rank = new RankTarget { Start = 1, End = 12 } },
            Events = [new GoalEvent { At = now, Type = GoalEventType.Created }],
        };
        db.Goals.Add(goal);
        var priority = 1;
        foreach (var projectId in projectIds)
        {
            var project = await db.Projects.SingleAsync(entity => entity.Id == projectId, TestContext.Current.CancellationToken);
            db.ProjectGoals.Add(ProjectGoalPlanningService.CreateMembership(project, goal, priority++, now));
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return goal.Id;
    }

    private static async Task<string?> KeyAsync(NpgsqlConnection connection, Guid goalId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT rank_target_key FROM project_goals WHERE goal_id = @goal;";
        command.Parameters.AddWithValue("goal", goalId);
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) as string;
    }

    /// <summary>Inserts a goal row plus an in-flight membership; a unique-index violation on the membership
    /// surfaces as a <see cref="PostgresException"/>.</summary>
    private static async Task InsertMembershipAsync(
        NpgsqlConnection connection, Guid projectId, string entityId, string goalType, string? rankKey,
        string entityType = "Character")
    {
        var goalId = Guid.NewGuid();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO goals (id, revision, profile_id, entity_type, entity_id, goal_type, status,
                               depends_on, created_at, updated_at, config, events)
            SELECT @goal, 0, profile_id, @entityType, @entityId, @goalType, 'Active',
                   ARRAY[]::uuid[], now(), now(), '{}', '[]'
            FROM projects WHERE id = @project;
            INSERT INTO project_goals (project_id, goal_id, priority, entity_type, entity_id, goal_type,
                                       occupies_in_flight_slot, rank_target_key, created_at)
            VALUES (@project, @goal, 99, @entityType, @entityId, @goalType, TRUE, @rankKey, now());
            """;
        command.Parameters.AddWithValue("goal", goalId);
        command.Parameters.AddWithValue("project", projectId);
        command.Parameters.AddWithValue("entityType", entityType);
        command.Parameters.AddWithValue("entityId", entityId);
        command.Parameters.AddWithValue("goalType", goalType);
        command.Parameters.AddWithValue("rankKey", (object?)rankKey ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
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
