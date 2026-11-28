using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.Domain.GuildRaids;
using TacticusPlanner.Domain.GuildRaids.Enums;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.TacticusApi;
using TacticusPlanner.TacticusApi.Models.GuildRaid;
using Testcontainers.PostgreSql;
using Xunit;
using TacticusGuildResponse = TacticusPlanner.TacticusApi.Models.Guild.GuildResponse;
using TacticusPlayerResponse = TacticusPlanner.TacticusApi.Models.Player.PlayerResponse;

namespace TacticusPlanner.Persistence.IntegrationTests;

public sealed class GuildRaidPersistencePostgresTests
{
    [Fact]
    public async Task RetryingExecutionStrategySupportsTransactionalRefreshPersistence()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(postgres.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure())
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var db = new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile());
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var now = DateTimeOffset.UtcNow;
        var guild = new Guild
        {
            Id = GuildId.From(Guid.NewGuid()),
            TacticusGuildId = TacticusGuildId.From(Guid.NewGuid().ToString()),
            TacticusGuildIdHash = TacticusGuildIdHash.From(Enumerable.Repeat((byte)4, 32).ToArray()),
            Tag = $"R{Guid.NewGuid():N}"[..10],
            Name = "Retrying raid refresh",
            GuildApiToken = "test-token",
            LastSyncSucceededAt = now,
        };
        db.Guilds.Add(guild);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // GuildRaidRefreshCoordinator resolves GuildRaidStatusService from its own DI scope (so a refresh
        // outlives any single caller's request scope), so this test wires a real service provider with the
        // same registrations production DI would use, rather than constructing the service by hand.
        await using var services = new ServiceCollection()
            .AddGameCatalog()
            .AddLogging()
            .AddSingleton<IColumnHashService>(new DeterministicHash())
            .AddSingleton<ITacticusApi>(new NoActiveSeasonTacticusApi())
            .AddSingleton(TimeProvider.System)
            .AddScoped(_ => new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile()))
            .AddScoped<GuildRaidStatusService>()
            .AddSingleton<GuildRaidRefreshCoordinator>()
            .AddScoped<GuildRaidEndedSeasonService>()
            .AddScoped<GuildPlayerRegistry>()
            .AddScoped<GuildClaimEvaluator>()
            .AddScoped<GuildSyncService>()
            .AddScoped<GuildRosterRefreshService>()
            .BuildServiceProvider();

        var coordinator = services.GetRequiredService<GuildRaidRefreshCoordinator>();
        var result = await coordinator.RunAsync(guild, TestContext.Current.CancellationToken);

        var success = Assert.IsType<GuildRaidRefreshResult.Success>(result);
        Assert.Equal(GuildRaidObservationState.NoActiveSeason, success.Response.State);
        var persisted = await db.GuildRaidSyncStates.SingleAsync(
            state => state.GuildId == guild.Id,
            TestContext.Current.CancellationToken);
        Assert.NotNull(persisted.ObservedAt);
        Assert.Null(persisted.ActiveSeasonId);
    }

    [Fact]
    public async Task MigrationEnforcesOwnershipAndIdempotency()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var db = new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile());
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var guild = new Guild
        {
            Id = GuildId.From(Guid.NewGuid()),
            TacticusGuildId = TacticusGuildId.From(Guid.NewGuid().ToString()),
            TacticusGuildIdHash = TacticusGuildIdHash.From(Enumerable.Repeat((byte)1, 32).ToArray()),
            Tag = $"T{Guid.NewGuid():N}"[..10],
            Name = "Raid persistence",
        };
        var season = new GuildRaidSeason
        {
            Id = GuildRaidSeasonId.From(Guid.NewGuid()),
            GuildId = guild.Id,
            SeasonNumber = 77,
            SeasonConfigId = "guild_boss_season_config_1",
            ObservedAt = DateTimeOffset.UtcNow,
        };
        var currentHash = TacticusUserIdHash.From(Enumerable.Repeat((byte)2, 32).ToArray());
        var otherHash = TacticusUserIdHash.From(Enumerable.Repeat((byte)3, 32).ToArray());
        var current = Attack(season.Id, currentHash, "current", DateTimeOffset.UtcNow.AddMinutes(-2));
        var other = Attack(season.Id, otherHash, "other", DateTimeOffset.UtcNow.AddMinutes(-1));
        current.Units.Add(Unit("current-unit"));
        other.Units.Add(Unit("other-unit"));
        season.Attacks.Add(current);
        season.Attacks.Add(other);
        db.AddRange(guild, season, new GuildRaidSyncState
        {
            GuildId = guild.Id,
            State = GuildRaidObservationState.Active,
            ObservedAt = season.ObservedAt,
            LastAttemptedAt = season.ObservedAt,
            ActiveSeasonId = season.Id,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Units are inline jsonb on their attack.
        await using (var read = new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile()))
        {
            var units = await read.GuildRaidAttacks.AsNoTracking()
                .Where(attack => attack.Id == current.Id)
                .Select(attack => attack.Units)
                .SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal("current-unit", Assert.Single(units).UnitId);
        }

        var duplicate = Attack(season.Id, currentHash, "duplicate", DateTimeOffset.UtcNow);
        duplicate.ContentHash = current.ContentHash;
        db.GuildRaidAttacks.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MigrationKeepsExistingAttacksAndGuildDeletionCascades()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(ct);
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile());
        await db.GetService<IMigrator>().MigrateAsync("AddGuildWars", ct);

        // Raw SQL: the model already has the columns this migration adds.
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO guilds (id, tacticus_guild_id, tag, name, level, revision, created_at, updated_at)
            VALUES ('00000000-0000-0000-0000-000000000001', 'guild', 'TAG', 'Guild', 1, 0, now(), now());
            INSERT INTO guild_raid_seasons (id, guild_id, season_number, season_config_id, observed_at, created_at, updated_at)
            VALUES ('00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000001', 77, 'config', now(), now(), now());
            INSERT INTO guild_raid_attacks (id, guild_raid_season_id, content_hash, tacticus_user_id_hash, tier, set,
                encounter_index, remaining_hp, maximum_hp, encounter_type, unit_set_id, progression_index, difficulty,
                damage_dealt, damage_type, completed_at, units)
            VALUES ('00000000-0000-0000-0000-000000000003', '00000000-0000-0000-0000-000000000002', 'old',
                decode(repeat('02', 32), 'hex'), 0, 0, 0, 0, 0, 'Boss', 'boss', 4, 'Common', 1, 'Battle', now(), '[]');
            """,
            ct);
        await db.Database.MigrateAsync(ct);

        var existing = await db.GuildRaidAttacks.AsNoTracking().SingleAsync(ct);
        Assert.Equal(4, existing.ProgressionIndex);
        Assert.Null(existing.TacticusUserIdSuffix);

        var guildId = GuildId.From(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var captured = Attack(
            GuildRaidSeasonId.From(Guid.Parse("00000000-0000-0000-0000-000000000002")),
            TacticusUserIdHash.From(Enumerable.Repeat((byte)2, 32).ToArray()),
            "captured",
            DateTimeOffset.UtcNow);
        captured.ProgressionIndex = null;
        captured.TacticusUserIdSuffix = "3bc4";
        db.GuildRaidAttacks.Add(captured);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        Assert.Null((await db.GuildRaidAttacks.AsNoTracking().SingleAsync(attack => attack.Id == captured.Id, ct)).ProgressionIndex);

        await db.Guilds.Where(guild => guild.Id == guildId).ExecuteDeleteAsync(ct);
        Assert.Equal(0, await db.GuildRaidAttacks.CountAsync(ct) + await db.GuildRaidSeasons.CountAsync(ct));
    }

    private static GuildRaidAttack Attack(
        GuildRaidSeasonId seasonId,
        TacticusUserIdHash userHash,
        string unitSetId,
        DateTimeOffset completedAt) => new()
        {
            Id = GuildRaidAttackId.From(Guid.NewGuid()),
            GuildRaidSeasonId = seasonId,
            ContentHash = $"{unitSetId}-{Guid.NewGuid():N}",
            TacticusUserIdHash = userHash,
            EncounterType = GuildRaidEncounterType.Boss,
            UnitSetId = unitSetId,
            ProgressionIndex = 1,
            Difficulty = GuildRaidDifficulty.Common,
            DamageType = GuildRaidDamageType.Battle,
            CompletedAt = completedAt,
        };

    private static GuildRaidAttackUnit Unit(string unitId) => new()
    {
        UnitId = unitId,
        Kind = GuildRaidAttackUnitKind.Hero,
    };

    private sealed class PassthroughEncryption : IColumnEncryptionService
    {
        public string? Encrypt(string? plaintext, string column) => plaintext;
        public string? Decrypt(string? envelope, string column) => envelope;
    }

    private sealed class NoProfile : ICurrentProfileProvider
    {
        public ProfileId? ProfileId => null;
    }

    private sealed class DeterministicHash : IColumnHashService
    {
        public byte[]? ComputeHash(string? value) => value is null ? null : Enumerable.Repeat((byte)5, 32).ToArray();
    }

    private sealed class NoActiveSeasonTacticusApi : ITacticusApi
    {
        public Task<TacticusPlayerResponse> GetPlayerAsync(
            string personalApiToken,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TacticusGuildResponse> GetGuildAsync(
            string guildApiToken,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GuildRaidResponse> GetGuildRaidsAsync(
            string guildApiToken,
            CancellationToken cancellationToken = default) => Task.FromResult(new GuildRaidResponse());

        public Task<GuildRaidResponse> GetGuildRaidBySeasonAsync(
            string guildApiToken,
            int season,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
