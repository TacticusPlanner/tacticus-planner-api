namespace TacticusPlanner.Api.Features.GameCatalog;

public sealed record GameCatalogManifestResponse(
    string Version,
    int SchemaVersion,
    string GameVersion,
    string SourceHash,
    IReadOnlyList<GameCatalogManifestDatasetResponse> Datasets
);

public sealed record GameCatalogManifestDatasetResponse(
    string Key,
    string Hash,
    string Url
);

/// <summary>Envelope for one served (denormalized) dataset. Payload shape varies per entity.</summary>
public sealed record GameCatalogDatasetEnvelope<TPayload>(
    string Version,
    int SchemaVersion,
    string GameVersion,
    string SourceHash,
    string DatasetKey,
    string DatasetHash,
    TPayload Data
);
