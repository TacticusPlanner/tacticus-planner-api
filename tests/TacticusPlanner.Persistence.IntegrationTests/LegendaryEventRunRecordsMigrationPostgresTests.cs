using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TacticusPlanner.Api.Features.LegendaryEventPlans;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.Persistence.Interceptors;
using Testcontainers.PostgreSql;
using Xunit;

namespace TacticusPlanner.Persistence.IntegrationTests;

/// <summary>
/// Relational coverage for <c>AddLegendaryEventRunRecords</c> (LRE Stage 7): the jsonb round trip of lanes, teams
/// and roster units through the recorder, the CHECKs, the unique snapshot reference, set-null when a snapshot is
/// removed, and the cascade from account purge.
/// </summary>
public sealed class LegendaryEventRunRecordsMigrationPostgresTests
{
    private const string EventId = "votanUthar";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RecorderRoundTripsAndConstraintsHold()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(Ct);
        var connectionString = postgres.GetConnectionString();
        var (options, profileId) = await MigrateAndSeedProfileAsync(connectionString);

        await RecordAsync(options, profileId, Progress(run: 2, [0, 1, 2]), rankUp: false);
        await RecordAsync(options, profileId, Progress(run: 2, [0, 1, 2, 3]), rankUp: true);

        await using (var db = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(profileId)))
        {
            var record = await db.LegendaryEventRunRecords.Include(entity => entity.RosterSnapshot).SingleAsync(Ct);
            Assert.Equal(2, record.Run);
            Assert.Equal([0, 1, 2, 3], Assert.Single(record.Lanes.Alpha!.Encounters).ObjectivesCleared);
            Assert.Null(record.Lanes.Beta);
            Assert.Empty(record.Teams);
            var unit = Assert.Single(record.RosterSnapshot!.Characters);
            Assert.Equal("ultraTigurius", unit.UnitId.Value);
            Assert.Equal(13, (int)unit.Rank);
            Assert.Equal(35, Assert.Single(unit.Abilities).Level);
        }

        Assert.Equal(1L, await CountAsync(connectionString, "roster_snapshots"));
        var snapshotId = await ScalarAsync(connectionString, "SELECT id FROM roster_snapshots");

        string[][] rejected =
        [
            ["ck_legendary_event_run_records_run",
                "INSERT INTO legendary_event_run_records (id, profile_id, event_id, run, started_at, progress_at, catalog_version, points, lanes, teams, created_at, updated_at) VALUES (gen_random_uuid(), @profile, 'votanUthar', 4, now(), now(), 'v', 0, '{}', '[]', now(), now())"],
            ["ix_legendary_event_run_records_roster_snapshot_id",
                "INSERT INTO legendary_event_run_records (id, profile_id, event_id, run, started_at, progress_at, catalog_version, points, lanes, teams, roster_snapshot_id, created_at, updated_at) VALUES (gen_random_uuid(), @profile, 'votanUthar', 1, now(), now(), 'v', 0, '{}', '[]', @snapshot, now(), now())"],
            ["ck_roster_snapshots_source",
                "INSERT INTO roster_snapshots (id, profile_id, source, taken_at, catalog_version, characters, mows, created_at, updated_at) VALUES (gen_random_uuid(), @profile, 'manual', now(), 'v', '[]', '[]', now(), now())"],
        ];
        foreach (var (constraint, sql) in rejected.Select(entry => (entry[0], entry[1])))
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
                connectionString, sql, ("profile", profileId.Value), ("snapshot", snapshotId)));
            Assert.Equal(constraint, exception.ConstraintName);
        }

        // The name column is varchar(60), so an over-long name fails on length before its CHECK runs.
        var tooLong = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString,
            $"INSERT INTO roster_snapshots (id, profile_id, source, name, taken_at, catalog_version, characters, mows, created_at, updated_at) VALUES (gen_random_uuid(), @profile, 'legendaryEventRun', '{new string('n', 61)}', now(), 'v', '[]', '[]', now(), now())",
            ("profile", profileId.Value)));
        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, tooLong.SqlState);

        await ExecuteAsync(connectionString, "DELETE FROM roster_snapshots");
        Assert.Equal(1L, await CountAsync(connectionString, "legendary_event_run_records"));
        Assert.Equal(1L, await CountAsync(connectionString,
            "legendary_event_run_records WHERE roster_snapshot_id IS NULL"));

        await RecordAsync(options, profileId, Progress(run: 3, [0]), rankUp: false);
        Assert.Equal(1L, await CountAsync(connectionString, "roster_snapshots"));

        await ExecuteAsync(connectionString, "DELETE FROM accounts");

        Assert.Equal(0L, await CountAsync(connectionString, "legendary_event_run_records"));
        Assert.Equal(0L, await CountAsync(connectionString, "roster_snapshots"));
    }

    private static LreProgressRecord Progress(int run, List<int> cleared) =>
        new()
        {
            Id = UnitId.From(EventId),
            Alpha = new LreTrackProgressRecord
            {
                Encounters = [new LreEncounterProgressRecord { ObjectivesCleared = cleared, HighScore = 30 }],
            },
            CurrentPoints = 100,
            CurrentEventRun = run,
        };

    private static async Task RecordAsync(
        DbContextOptions<PlannerDbContext> options, ProfileId profileId, LreProgressRecord progress, bool rankUp)
    {
        var character = new PlayerCharacterRecord
        {
            UnitId = UnitId.From("ultraTigurius"),
            XpLevel = 35,
            Rank = (TacticusPlanner.GameDomain.UnitRank)(rankUp ? 13 : 12),
            Abilities = [new PlayerUnitAbilityRecord { AbilityId = "StormOfWrath", Level = 35 }],
            AppliedUpgradeSlots = [0, 2],
        };
        await using var db = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(profileId));
        await new LegendaryEventRunRecordRecorder(db, new Catalog())
            .ApplyAsync(profileId, [progress], [character], [], DateTimeOffset.UtcNow, Ct);
        await db.SaveChangesAsync(Ct);
    }

    private static async Task ExecuteAsync(string connectionString, string sql, params (string Name, Guid Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<Guid> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (Guid)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static async Task<long> CountAsync(string connectionString, string tableAndFilter)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {tableAndFilter}";
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static async Task<(DbContextOptions<PlannerDbContext> Options, ProfileId ProfileId)> MigrateAndSeedProfileAsync(
        string connectionString)
    {
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new EntityMetadataInterceptor(TimeProvider.System))
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using (var migrationDb = new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile()))
        {
            await migrationDb.Database.GetService<IMigrator>().MigrateAsync(cancellationToken: Ct);
        }

        var profileId = ProfileId.From(Guid.NewGuid());
        await ExecuteAsync(connectionString, """
            INSERT INTO accounts (id, issuer, subject, created_at, updated_at)
            VALUES (gen_random_uuid(), 'test', 'legendary-event-run-records', now(), now());
            INSERT INTO profiles (id, account_id, display_name, created_at, updated_at)
            SELECT @profile, id, 'LRE runs', now(), now() FROM accounts LIMIT 1;
            """, ("profile", profileId.Value));
        return (options, profileId);
    }

    private sealed class Catalog : IGameCatalogProvider
    {
        public GameCatalogSnapshot Current { get; } = GameCatalogLoader.Load();
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
