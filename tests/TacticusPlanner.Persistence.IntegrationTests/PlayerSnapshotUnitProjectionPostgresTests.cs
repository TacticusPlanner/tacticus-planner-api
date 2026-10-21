using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameDomain;
using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.Persistence.Interceptors;
using Testcontainers.PostgreSql;
using Xunit;

namespace TacticusPlanner.Persistence.IntegrationTests;

/// <summary>
/// Spike for <c>optimize-api-handlers</c> 3.1: goal-target validation needs one character and one MoW out of
/// the player-data snapshot, so EF Core must translate <c>FirstOrDefault</c> over the owned JSON collections
/// to SQL instead of materializing the whole row.
/// </summary>
public sealed class PlayerSnapshotUnitProjectionPostgresTests
{
    [Fact]
    public async Task TargetedUnitsAreProjectedFromTheOwnedJsonCollectionsInSql()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(ct);
        var (options, profileId) = await MigrateAndSeedProfileAsync(postgres.GetConnectionString());
        await using (var seed = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(profileId)))
        {
            seed.PlayerDataSnapshots.Add(new PlayerDataSnapshot
            {
                Id = profileId,
                ConfigHash = "config",
                SourceHash = "source",
                SyncedAt = DateTimeOffset.UtcNow,
                Characters =
                [
                    Character("other", rank: UnitRank.Stone1),
                    Character("ragnar", rank: UnitRank.Gold2),
                ],
                Mows = [new PlayerMowRecord { UnitId = UnitId.From("astra"), Abilities = [new() { AbilityId = "a1", Level = 4 }] }],
            });
            await seed.SaveChangesAsync(ct);
        }

        await using var db = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(profileId));
        var unitId = UnitId.From("ragnar");
        var mowId = UnitId.From("astra");
        var query = db.PlayerDataSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.Id == profileId)
            .Select(snapshot => new
            {
                Character = snapshot.Characters.FirstOrDefault(unit => unit.UnitId == unitId),
                Mow = snapshot.Mows.FirstOrDefault(unit => unit.UnitId == mowId),
            });

        var sql = query.ToQueryString();
        var result = await query.SingleAsync(ct);

        Assert.Contains("jsonb_to_recordset(p.characters)", sql);
        Assert.Contains("WHERE c.\"UnitId\" = @unitId", sql);
        Assert.NotNull(result.Character);
        Assert.Equal(UnitRank.Gold2, result.Character.Rank);
        Assert.Equal(["a1", "a2"], result.Character.Abilities.Select(ability => ability.AbilityId));
        Assert.Equal("slot-1", Assert.Single(result.Character.EquippedItems).SlotId);
        Assert.NotNull(result.Mow);
        Assert.Equal(4, Assert.Single(result.Mow.Abilities).Level);
        // Only the two unit chunks are read; the other nine JSON columns are never selected.
        Assert.DoesNotContain("inventory_upgrades", sql);
        Assert.DoesNotContain("live_progress", sql);
        Assert.DoesNotContain("campaign_progress", sql);
    }

    [Fact]
    public async Task AbsentUnitsProjectToNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(ct);
        var (options, profileId) = await MigrateAndSeedProfileAsync(postgres.GetConnectionString());
        await using (var seed = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(profileId)))
        {
            seed.PlayerDataSnapshots.Add(new PlayerDataSnapshot
            {
                Id = profileId,
                ConfigHash = "config",
                SourceHash = "source",
                SyncedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(ct);
        }

        await using var db = new PlannerDbContext(options, new PassthroughEncryption(), new StaticProfile(profileId));
        var unitId = UnitId.From("ragnar");
        var result = await db.PlayerDataSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.Id == profileId)
            .Select(snapshot => new { Character = snapshot.Characters.FirstOrDefault(unit => unit.UnitId == unitId) })
            .SingleAsync(ct);

        Assert.Null(result.Character);
    }

    private static PlayerCharacterRecord Character(string id, UnitRank rank) => new()
    {
        UnitId = UnitId.From(id),
        Rank = rank,
        Abilities = [new() { AbilityId = "a1", Level = 3 }, new() { AbilityId = "a2", Level = 5 }],
        EquippedItems = [new() { SlotId = "slot-1", EquipmentId = "eq", Level = 2 }],
    };

    private static async Task<(DbContextOptions<PlannerDbContext> Options, ProfileId ProfileId)> MigrateAndSeedProfileAsync(
        string connectionString)
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
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
            INSERT INTO profiles (id, issuer, subject, display_name, created_at, updated_at)
            VALUES (@profile, 'test', 'snapshot-projection', 'Snapshot projection', now(), now());
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
