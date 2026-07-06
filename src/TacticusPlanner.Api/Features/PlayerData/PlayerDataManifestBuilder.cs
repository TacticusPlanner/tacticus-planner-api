using PlayerDataSnapshotEntity = TacticusPlanner.Persistence.Users.PlayerData.PlayerDataSnapshot;

namespace TacticusPlanner.Api.Features.PlayerData;

/// <summary>Builds the served <see cref="PlayerDataManifest"/>, mirroring how
/// <c>GameCatalogSnapshot.Manifest</c> is derived from the catalog snapshot. The
/// <c>(schemaVersion, configHash, sourceHash, syncedAt, chunkHashes)</c> overload lets callers build the
/// manifest from a narrow query projection instead of a fully-materialized snapshot entity.</summary>
internal static class PlayerDataManifestBuilder
{
    public static PlayerDataManifest Build(PlayerDataSnapshotEntity snapshot) =>
        Build(snapshot.SchemaVersion, snapshot.ConfigHash, snapshot.SourceHash, snapshot.SyncedAt, snapshot.ChunkHashes);

    public static PlayerDataManifest Build(
        int schemaVersion,
        string configHash,
        string sourceHash,
        DateTimeOffset syncedAt,
        IReadOnlyDictionary<string, string> chunkHashes) => new(
        SchemaVersion: schemaVersion,
        GameConfigHash: configHash,
        SourceHash: sourceHash,
        SyncedAt: syncedAt,
        Chunks: PlayerDataChunkKeys.All
            .Select(key => new PlayerDataManifestChunk(
                key,
                chunkHashes.GetValueOrDefault(key, string.Empty),
                $"{PlayerDataChunkKeys.RoutePrefix}/{key}"))
            .ToArray());
}
