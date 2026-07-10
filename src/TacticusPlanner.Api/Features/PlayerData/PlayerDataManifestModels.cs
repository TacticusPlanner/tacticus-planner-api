namespace TacticusPlanner.Api.Features.PlayerData;

/// <summary>
/// The player-data manifest, mirroring <c>GameCatalogManifest</c>'s shape: release/version metadata plus
/// per-chunk hash and download url, so the client can determine which chunks changed and need
/// re-downloading. Unlike the catalog manifest, this is per-profile and authenticated.
/// </summary>
public sealed record PlayerDataManifest(
    int SchemaVersion,
    string GameConfigHash,
    string SourceHash,
    DateTimeOffset SyncedAt,
    IReadOnlyList<PlayerDataManifestChunk> Chunks
);

public sealed record PlayerDataManifestChunk(
    string Key,
    string Hash,
    string Url
);

/// <summary>Envelope for one served player-data chunk, mirroring <c>GameCatalogDatasetEnvelope&lt;T&gt;</c>.</summary>
public sealed record PlayerDataChunkEnvelope<TPayload>(
    int SchemaVersion,
    string GameConfigHash,
    string SourceHash,
    string ChunkKey,
    string ChunkHash,
    TPayload Data
);
