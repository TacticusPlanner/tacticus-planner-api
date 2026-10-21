using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NpgsqlTypes;
using TacticusPlanner.Api.Features.PlayerData;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.Persistence.Interceptors;
using Testcontainers.PostgreSql;
using Xunit;

namespace TacticusPlanner.Persistence.IntegrationTests;

/// <summary>
/// Guards <c>PlayerDataSyncService</c>'s direct jsonb chunk writes: a row written by that serializer must read
/// back through EF exactly like a row EF wrote itself. Every property of every chunk is populated by
/// reflection, so a new property, enum or value converter that the serializer mishandles fails here.
/// </summary>
public sealed class PlayerSnapshotJsonRoundTripPostgresTests
{
    [Fact]
    public async Task DirectlyWrittenChunksReadBackExactlyLikeEfWrittenOnes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(ct);
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new EntityMetadataInterceptor(TimeProvider.System))
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using (var migrationDb = new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile()))
        {
            await migrationDb.Database.GetService<IMigrator>().MigrateAsync(cancellationToken: ct);
        }

        var efProfile = await SeedProfileAsync(postgres.GetConnectionString(), "ef");
        var rawProfile = await SeedProfileAsync(postgres.GetConnectionString(), "raw");
        var transformed = (PlayerDataTransformResult)new Populator().Create(typeof(PlayerDataTransformResult))!;

        await using (var db = new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile()))
        {
            db.PlayerDataSnapshots.Add(Snapshot(efProfile, transformed));
            db.PlayerDataSnapshots.Add(new PlayerDataSnapshot { Id = rawProfile, ConfigHash = "c", SourceHash = "s", SyncedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
        }

        var chunks = PlayerDataSyncService.SerializeChunks(transformed);
        Assert.Equal(11, chunks.Count);
        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE player_data_snapshots SET "
                + string.Join(", ", chunks.Keys.Select(column => $"{column} = @{column}"))
                + " WHERE id = @id";
            foreach (var (column, json) in chunks)
            {
                command.Parameters.Add(new NpgsqlParameter(column, NpgsqlDbType.Jsonb) { Value = json });
            }

            command.Parameters.AddWithValue("id", rawProfile.Value);
            Assert.Equal(1, await command.ExecuteNonQueryAsync(ct));
        }

        await using var read = new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile());
        var ef = await read.PlayerDataSnapshots.IgnoreQueryFilters().AsNoTracking().SingleAsync(entity => entity.Id == efProfile, ct);
        var raw = await read.PlayerDataSnapshots.IgnoreQueryFilters().AsNoTracking().SingleAsync(entity => entity.Id == rawProfile, ct);
        foreach (var chunk in new Func<PlayerDataSnapshot, object>[]
        {
            s => s.PlayerDetails, s => s.Characters, s => s.Mows, s => s.InventoryUpgrades, s => s.InventoryItems,
            s => s.InventoryShards, s => s.Inventory, s => s.CampaignProgress, s => s.CampaignEventsProgress,
            s => s.LiveProgress, s => s.LreProgress,
        })
        {
            Assert.Equal(JsonSerializer.Serialize(chunk(ef)), JsonSerializer.Serialize(chunk(raw)));
        }

        // The populated fixture is non-trivial: the compared chunks are not just empty defaults.
        Assert.NotEmpty(raw.Characters);
        Assert.NotEmpty(raw.Characters[0].Abilities);
        Assert.NotEmpty(raw.Characters[0].EquippedItems);
        Assert.NotEmpty(raw.LreProgress);
        Assert.NotEmpty(raw.LiveProgress.BattleAttempts);
    }

    private static PlayerDataSnapshot Snapshot(ProfileId id, PlayerDataTransformResult t) => new()
    {
        Id = id,
        ConfigHash = "c",
        SourceHash = "s",
        SyncedAt = DateTimeOffset.UtcNow,
        PlayerDetails = t.PlayerDetails,
        Characters = t.Characters,
        Mows = t.Mows,
        InventoryUpgrades = t.InventoryUpgrades,
        InventoryItems = t.InventoryItems,
        InventoryShards = t.InventoryShards,
        Inventory = t.Inventory,
        CampaignProgress = t.CampaignProgress,
        CampaignEventsProgress = t.CampaignEventsProgress,
        LiveProgress = t.LiveProgress,
        LreProgress = t.LreProgress,
    };

    private static async Task<ProfileId> SeedProfileAsync(string connectionString, string name)
    {
        var profileId = ProfileId.From(Guid.NewGuid());
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO profiles (id, issuer, subject, display_name, created_at, updated_at)
            VALUES (@profile, 'test', @name, @name, now(), now());
            """;
        command.Parameters.AddWithValue("profile", profileId.Value);
        command.Parameters.AddWithValue("name", name);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return profileId;
    }

    /// <summary>Builds an instance of any chunk-shaped type with every property set to a distinct non-default
    /// value: two-element collections, non-default enums, filled nullables and value objects.</summary>
    private sealed class Populator
    {
        private int counter;

        public object? Create(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying is not null)
            {
                return Create(underlying);
            }

            counter++;
            if (type == typeof(string)) return $"v{counter}";
            if (type == typeof(bool)) return true;
            if (type == typeof(int)) return counter;
            if (type == typeof(long)) return (long)counter + 10_000_000_000;
            if (type == typeof(double)) return counter + 0.5;
            if (type == typeof(decimal)) return counter + 0.25m;
            if (type == typeof(Guid)) return Guid.NewGuid();
            if (type == typeof(DateTimeOffset)) return new DateTimeOffset(2026, 1, 1, 0, 0, counter % 60, TimeSpan.Zero);
            if (type.IsEnum)
            {
                var values = Enum.GetValues(type).Cast<object>().ToArray();
                return values[Math.Min(1 + counter % 3, values.Length - 1)];
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)Activator.CreateInstance(type)!;
                list.Add(Create(type.GetGenericArguments()[0]));
                list.Add(Create(type.GetGenericArguments()[0]));
                return list;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var dictionary = (IDictionary)Activator.CreateInstance(type)!;
                dictionary[Create(type.GetGenericArguments()[0])!] = Create(type.GetGenericArguments()[1]);
                return dictionary;
            }

            var from = type.GetMethod("From", BindingFlags.Public | BindingFlags.Static, [typeof(string)])
                ?? type.GetMethod("From", BindingFlags.Public | BindingFlags.Static, [typeof(Guid)])
                ?? type.GetMethod("From", BindingFlags.Public | BindingFlags.Static, [typeof(int)]);
            if (from is not null)
            {
                return from.Invoke(null, [Create(from.GetParameters()[0].ParameterType)]);
            }

            var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            var instance = constructor.Invoke(constructor.GetParameters().Select(p => Create(p.ParameterType)).ToArray());
            foreach (var property in type.GetProperties().Where(p => p.CanWrite && p.GetIndexParameters().Length == 0))
            {
                property.SetValue(instance, Create(property.PropertyType));
            }

            return instance;
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
}
