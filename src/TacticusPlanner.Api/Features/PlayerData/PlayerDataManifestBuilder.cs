using PlayerDataSnapshotEntity = TacticusPlanner.Persistence.Users.PlayerData.PlayerDataSnapshot;

namespace TacticusPlanner.Api.Features.PlayerData;

/// <summary>Builds the served <see cref="PlayerDataManifest"/>, mirroring how
/// <c>GameCatalogSnapshot.Manifest</c> is derived from the catalog snapshot.</summary>
internal static class PlayerDataManifestBuilder
{
    public static PlayerDataManifest Build(PlayerDataSnapshotEntity snapshot) => new(
        SchemaVersion: snapshot.SchemaVersion,
        GameConfigHash: snapshot.ConfigHash,
        SourceHash: snapshot.SourceHash,
        SyncedAt: snapshot.SyncedAt,
        Chunks: PlayerDataChunkKeys.All
            .Select(key => new PlayerDataManifestChunk(
                key,
                snapshot.ChunkHashes.GetValueOrDefault(key, string.Empty),
                $"{PlayerDataChunkKeys.RoutePrefix}/{key}"))
            .ToArray());
}
