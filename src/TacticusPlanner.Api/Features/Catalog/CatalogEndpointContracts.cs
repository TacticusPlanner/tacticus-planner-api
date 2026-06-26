namespace TacticusPlanner.Api.Features.Catalog;

public sealed record CatalogManifestResponse(
    string Version,
    int SchemaVersion,
    string GameVersion,
    string SourceHash,
    IReadOnlyList<CatalogManifestDatasetResponse> Datasets
);

public sealed record CatalogManifestDatasetResponse(
    string Key,
    string Hash,
    string Url
);

/// <summary>Envelope for one served (denormalized) dataset. Payload shape varies per entity.</summary>
public sealed record CatalogDatasetEnvelope<TPayload>(
    string Version,
    int SchemaVersion,
    string GameVersion,
    string SourceHash,
    string DatasetKey,
    string DatasetHash,
    TPayload Data
);
