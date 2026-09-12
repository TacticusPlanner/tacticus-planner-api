using Microsoft.EntityFrameworkCore;
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
            .AddSingleton<IColumnHashService>(new DeterministicHash())
            .AddSingleton<ITacticusApi>(new NoActiveSeasonTacticusApi())
            .AddSingleton(TimeProvider.System)
            .AddScoped(_ => new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile()))
            .AddScoped<GuildRaidStatusService>()
            .AddSingleton<GuildRaidRefreshCoordinator>()
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
    public async Task MigrationEnforcesOwnershipIdempotencyAndSelectiveCurrentUserQuery()
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
        current.Units.Add(Unit(current.Id, "current-unit"));
        other.Units.Add(Unit(other.Id, "other-unit"));
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

        db.ChangeTracker.Clear();
        var repository = new GuildRaidAttackRepository(db);
        var sql = repository.QueryCurrentUserAttacks(season.Id, currentHash).ToQueryString();
        Assert.Contains("guild_raid_season_id", sql);
        Assert.Contains("tacticus_user_id_hash", sql);
        Assert.Contains("ORDER BY", sql);
        Assert.Contains("completed_at", sql);
        Assert.DoesNotContain("guild_raid_attack_units", sql);

        var projection = await repository.GetCurrentUserAttacksAsync(
            season.Id,
            currentHash,
            includeUnits: true,
            TestContext.Current.CancellationToken);
        var selected = Assert.Single(projection);
        Assert.Equal("current", selected.UnitSetId);
        Assert.Equal("current-unit", Assert.Single(selected.Units).UnitId);
        Assert.DoesNotContain(projection, attack => attack.UnitSetId == "other");

        var duplicate = Attack(season.Id, currentHash, "duplicate", DateTimeOffset.UtcNow);
        duplicate.ContentHash = current.ContentHash;
        db.GuildRaidAttacks.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            db.SaveChangesAsync(TestContext.Current.CancellationToken));
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

    private static GuildRaidAttackUnit Unit(GuildRaidAttackId attackId, string unitId) => new()
    {
        Id = GuildRaidAttackUnitId.From(Guid.NewGuid()),
        GuildRaidAttackId = attackId,
        UnitId = unitId,
        Kind = GuildRaidAttackUnitKind.Hero,
    };

    private sealed class PassthroughEncryption : IColumnEncryptionService
    {
        public string? Encrypt(string? plaintext) => plaintext;
        public string? Decrypt(string? envelope) => envelope;
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
