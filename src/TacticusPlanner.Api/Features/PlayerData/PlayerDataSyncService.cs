using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Refit;
using TacticusPlanner.Api.Features.LegendaryEventPlans;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;
using TacticusPlanner.TacticusApi;
using PlayerDataSnapshotEntity = TacticusPlanner.Domain.PlayerData.PlayerDataSnapshot;

namespace TacticusPlanner.Api.Features.PlayerData;

/// <summary>
/// Fetches the current player from the Tacticus API, transforms it, and persists it as a
/// <see cref="PlayerDataSnapshotEntity"/> — the sync logic <see cref="PlayerSyncEndpoint"/> exposes
/// directly, extracted so <c>ImportV1ProfileEndpoint</c> can also call it: a V1 import that selects
/// goals needs a snapshot to exist before <c>V1GoalImportService</c> runs, not on whatever later
/// request happens to trigger the player-data provider's own sync (rewrite-v1-goal-import's
/// player_data_required refusal was otherwise near-guaranteed on a brand-new account's first import).
/// </summary>
public sealed class PlayerDataSyncService(
    PlannerDbContext db,
    ITacticusApi tacticusApi,
    PlayerDataTransformer transformer,
    LegendaryEventRunRecordRecorder runRecordRecorder,
    TimeProvider timeProvider)
{
    private static readonly IReadOnlyDictionary<string, Action<PlayerDataSnapshotEntity, PlayerDataTransformResult>> ChunkSetters =
        new Dictionary<string, Action<PlayerDataSnapshotEntity, PlayerDataTransformResult>>(StringComparer.Ordinal)
        {
            [PlayerDataChunkKeys.PlayerDetails] = (s, t) => s.PlayerDetails = t.PlayerDetails,
            [PlayerDataChunkKeys.Characters] = (s, t) => s.Characters = t.Characters,
            [PlayerDataChunkKeys.Mows] = (s, t) => s.Mows = t.Mows,
            [PlayerDataChunkKeys.InventoryUpgrades] = (s, t) => s.InventoryUpgrades = t.InventoryUpgrades,
            [PlayerDataChunkKeys.InventoryItems] = (s, t) => s.InventoryItems = t.InventoryItems,
            [PlayerDataChunkKeys.InventoryShards] = (s, t) => s.InventoryShards = t.InventoryShards,
            [PlayerDataChunkKeys.Inventory] = (s, t) => s.Inventory = t.Inventory,
            [PlayerDataChunkKeys.CampaignProgress] = (s, t) => s.CampaignProgress = t.CampaignProgress,
            [PlayerDataChunkKeys.CampaignEventsProgress] = (s, t) => s.CampaignEventsProgress = t.CampaignEventsProgress,
            [PlayerDataChunkKeys.LiveProgress] = (s, t) => s.LiveProgress = t.LiveProgress,
            [PlayerDataChunkKeys.LreProgress] = (s, t) => s.LreProgress = t.LreProgress,
        };

    // EF Core writes the owned-JSON chunks with CLR property names, Vogen string converters and numeric enums.
    // Enums such as UnitProgression carry a [JsonConverter] writing wire strings that EF cannot read back, so
    // every enum property is forced to a number.
    private static readonly JsonSerializerOptions ChunkJsonOptions = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { StoreEnumsAsNumbers } },
    };

    private static void StoreEnumsAsNumbers(JsonTypeInfo typeInfo)
    {
        foreach (var property in typeInfo.Properties)
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (type.IsEnum)
            {
                property.CustomConverter = (JsonConverter)Activator.CreateInstance(
                    typeof(EnumAsNumberConverter<>).MakeGenericType(type))!;
            }
        }
    }

    private sealed class EnumAsNumberConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            (T)Enum.ToObject(typeof(T), reader.GetInt64());

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static readonly (string Key, string Column, Func<PlayerDataTransformResult, object> Serialize)[] ChunkColumns =
    [
        (PlayerDataChunkKeys.PlayerDetails, "player_details", t => t.PlayerDetails),
        (PlayerDataChunkKeys.Characters, "characters", t => t.Characters),
        (PlayerDataChunkKeys.Mows, "mows", t => t.Mows),
        (PlayerDataChunkKeys.InventoryUpgrades, "inventory_upgrades", t => t.InventoryUpgrades),
        (PlayerDataChunkKeys.InventoryItems, "inventory_items", t => t.InventoryItems),
        (PlayerDataChunkKeys.InventoryShards, "inventory_shards", t => t.InventoryShards),
        (PlayerDataChunkKeys.Inventory, "inventory", t => t.Inventory),
        (PlayerDataChunkKeys.CampaignProgress, "campaign_progress", t => t.CampaignProgress),
        (PlayerDataChunkKeys.CampaignEventsProgress, "campaign_events_progress", t => t.CampaignEventsProgress),
        (PlayerDataChunkKeys.LiveProgress, "live_progress", t => t.LiveProgress),
        (PlayerDataChunkKeys.LreProgress, "lre_progress", t => t.LreProgress),
    ];

    /// <summary>The jsonb text this service writes for each chunk column of a changed transform result.</summary>
    public static IReadOnlyDictionary<string, string> SerializeChunks(PlayerDataTransformResult transformed) =>
        ChunkColumns.ToDictionary(
            chunk => chunk.Column,
            chunk => JsonSerializer.Serialize(chunk.Serialize(transformed), ChunkJsonOptions),
            StringComparer.Ordinal);

    public async Task<PlayerDataSyncResult> SyncAsync(ProfileId profileId, string apiKey, CancellationToken ct)
    {
        // Hashes and revision are read before the upstream fetch, so a slow response can never adopt a revision
        // another request committed meanwhile: the write below is predicated on exactly this revision.
        var stored = await ReadStoredAsync(profileId, ct);
        var attemptedAt = timeProvider.GetUtcNow();
        var integration = await LoadIntegrationAsync(profileId, attemptedAt, ct);

        TacticusApi.Models.Player.PlayerResponse response;
        try
        {
            response = await tacticusApi.GetPlayerAsync(apiKey, ct);
        }
        catch (ApiException exception) when ((int)exception.StatusCode is 400 or 401 or 403 or 404)
        {
            await db.SaveChangesAsync(ct);
            return new PlayerDataSyncResult.UpstreamRejected();
        }

        var transformed = transformer.Transform(response);
        var syncedAt = timeProvider.GetUtcNow();

        try
        {
            return await WriteAsync(profileId, stored, transformed, syncedAt, integration, ct);
        }
        catch (DbUpdateException exception) when (stored is null && IsSnapshotPrimaryKeyConflict(exception))
        {
            // Two concurrent first-time syncs (a V1 import's first request commonly races a direct sync): the
            // loser's insert was rejected and its transaction rolled back. If the winner stored identical
            // content, converge as an update on the winner's revision; otherwise report the conflict rather than
            // replaying this stale input over the new row.
            db.ChangeTracker.Clear();
            stored = await ReadStoredAsync(profileId, ct)
                ?? throw new DbUpdateConcurrencyException("The player-data snapshot changed during the sync.");
            if (!stored.ChunkHashes.OrderBy(pair => pair.Key).SequenceEqual(transformed.ChunkHashes.OrderBy(pair => pair.Key)))
            {
                throw new DbUpdateConcurrencyException("The player-data snapshot changed during the sync.");
            }

            integration = await LoadIntegrationAsync(profileId, attemptedAt, ct);
            return await WriteAsync(profileId, stored, transformed, syncedAt, integration, ct);
        }
    }

    private async Task<StoredSnapshot?> ReadStoredAsync(ProfileId profileId, CancellationToken ct)
    {
        var row = await db.PlayerDataSnapshots.AsNoTracking()
            .Where(entity => entity.Id == profileId)
            .Select(entity => new { entity.Revision, entity.ChunkHashes })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : new StoredSnapshot(row.Revision, row.ChunkHashes);
    }

    private async Task<Domain.Profiles.TacticusIntegration?> LoadIntegrationAsync(ProfileId profileId, DateTimeOffset attemptedAt, CancellationToken ct)
    {
        var integration = await db.TacticusIntegrations.FirstOrDefaultAsync(entity => entity.Id == profileId, ct);
        if (integration is not null)
        {
            integration.TacticusSyncLastAttemptedAt = attemptedAt;
        }

        return integration;
    }

    /// <summary>The snapshot write, run records and success metadata in one transaction: any failure, including a
    /// revision conflict, rolls all of it back.</summary>
    private async Task<PlayerDataSyncResult> WriteAsync(
        ProfileId profileId,
        StoredSnapshot? stored,
        PlayerDataTransformResult transformed,
        DateTimeOffset syncedAt,
        Domain.Profiles.TacticusIntegration? integration,
        CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            PlayerDataSnapshotEntity snapshot;
            if (stored is null)
            {
                snapshot = new PlayerDataSnapshotEntity { Id = profileId };
                ApplyTransform(snapshot, transformed, isNew: true, syncedAt);
                db.PlayerDataSnapshots.Add(snapshot);
            }
            else
            {
                await UpdateChangedChunksAsync(profileId, stored, transformed, syncedAt, ct);
                snapshot = new PlayerDataSnapshotEntity
                {
                    Id = profileId,
                    ConfigHash = transformed.ConfigHash,
                    SourceHash = transformed.SourceHash,
                    SchemaVersion = PlayerDataTransformer.CurrentSchemaVersion,
                    SyncedAt = syncedAt,
                    ChunkHashes = transformed.ChunkHashes,
                };
            }

            await RecordRunsAsync(profileId, transformed, syncedAt, ct);
            if (integration is not null)
            {
                integration.TacticusSyncLastSucceededAt = syncedAt;
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                runRecordRecorder.Discard();
                throw;
            }

            await transaction.CommitAsync(ct);
            return new PlayerDataSyncResult.Success(snapshot);
        });
    }

    /// <summary>Writes only the chunks whose hash changed, plus the metadata, as one statement predicated on the
    /// revision read before the upstream fetch. <c>ExecuteUpdateAsync</c> cannot set the owned-JSON chunk columns
    /// and a partially loaded tracked entity makes EF rewrite every sibling JSON column, so the chunk JSON is
    /// serialized directly; <c>PlayerDataSnapshotJsonRoundTripPostgresTests</c> guards that format against EF's.</summary>
    private async Task UpdateChangedChunksAsync(
        ProfileId profileId,
        StoredSnapshot stored,
        PlayerDataTransformResult transformed,
        DateTimeOffset syncedAt,
        CancellationToken ct)
    {
        var assignments = new List<string>
        {
            "config_hash = @config_hash",
            "tacticus_last_updated_on = @last_updated_on",
            "source_hash = @source_hash",
            "schema_version = @schema_version",
            "synced_at = @synced_at",
            "chunk_hashes = @chunk_hashes",
            "revision = revision + 1",
            "updated_at = @updated_at",
        };
        var parameters = new List<NpgsqlParameter>
        {
            new("id", profileId.Value),
            new("revision", stored.Revision),
            new("config_hash", transformed.ConfigHash),
            new("last_updated_on", transformed.TacticusLastUpdatedOn),
            new("source_hash", transformed.SourceHash),
            new("schema_version", PlayerDataTransformer.CurrentSchemaVersion),
            new("synced_at", syncedAt),
            Json("chunk_hashes", transformed.ChunkHashes),
            new("updated_at", timeProvider.GetUtcNow()),
        };
        foreach (var (key, column, serialize) in ChunkColumns)
        {
            if (stored.ChunkHashes.GetValueOrDefault(key, string.Empty) == transformed.ChunkHashes[key])
            {
                continue;
            }

            assignments.Add($"{column} = @{column}");
            parameters.Add(Json(column, serialize(transformed)));
        }

        // Only constant column names are concatenated; every value is a parameter.
        var sql = "UPDATE player_data_snapshots SET " + string.Join(", ", assignments)
            + " WHERE id = @id AND revision = @revision";
        var rows = await db.Database.ExecuteSqlRawAsync(sql, parameters.Cast<object>().ToArray(), ct);
        if (rows != 1)
        {
            throw new DbUpdateConcurrencyException("The player-data snapshot changed during the sync.");
        }
    }

    private static NpgsqlParameter Json(string name, object value) =>
        new(name, NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(value, ChunkJsonOptions) };

    /// <summary>Opens or refreshes Legendary Event run records in the same unit of work as the snapshot
    /// (LRE Stage 7).</summary>
    private Task RecordRunsAsync(
        ProfileId profileId, PlayerDataTransformResult transformed, DateTimeOffset syncedAt, CancellationToken ct) =>
        runRecordRecorder.ApplyAsync(
            profileId, transformed.LreProgress, transformed.Characters, transformed.Mows, syncedAt, ct);

    private static void ApplyTransform(
        PlayerDataSnapshotEntity snapshot, PlayerDataTransformResult transformed, bool isNew, DateTimeOffset syncedAt)
    {
        var existingChunkHashes = snapshot.ChunkHashes;

        snapshot.ConfigHash = transformed.ConfigHash;
        snapshot.TacticusLastUpdatedOn = transformed.TacticusLastUpdatedOn;
        snapshot.SourceHash = transformed.SourceHash;
        snapshot.SchemaVersion = PlayerDataTransformer.CurrentSchemaVersion;
        snapshot.SyncedAt = syncedAt;
        snapshot.ChunkHashes = transformed.ChunkHashes;

        foreach (var (key, setter) in ChunkSetters)
        {
            var changed = isNew
                || existingChunkHashes.GetValueOrDefault(key, string.Empty) != transformed.ChunkHashes[key];

            if (changed)
            {
                setter(snapshot, transformed);
            }
        }
    }

    private static bool IsSnapshotPrimaryKeyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

internal sealed record StoredSnapshot(long Revision, Dictionary<string, string> ChunkHashes);

public abstract record PlayerDataSyncResult
{
    public sealed record Success(PlayerDataSnapshotEntity Snapshot) : PlayerDataSyncResult;

    public sealed record UpstreamRejected : PlayerDataSyncResult;
}
