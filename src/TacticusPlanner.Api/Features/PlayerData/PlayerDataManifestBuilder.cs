using PlayerDataSnapshotEntity = TacticusPlanner.Domain.PlayerData.PlayerDataSnapshot;

namespace TacticusPlanner.Api.Features.PlayerData;

/// <summary>Builds the served <see cref="PlayerDataManifest"/>, mirroring how
/// <c>GameCatalogSnapshot.Manifest</c> is derived from the catalog snapshot.</summary>
internal static class PlayerDataManifestBuilder
{
    public static PlayerDataManifest Build(PlayerDataSnapshotEntity snapshot) => Build(
        snapshot.SchemaVersion,
        snapshot.ConfigHash,
        snapshot.SourceHash,
        snapshot.SyncedAt,
        snapshot.ChunkHashes);

    /// <summary>Scalar overload so an unchanged-since-last-sync response can be built straight off a
    /// metadata-only (<c>AsNoTracking</c>) projection, without ever loading the full tracked entity —
    /// see <see cref="PlayerSyncEndpoint"/>'s fast path.</summary>
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
