namespace TacticusPlanner.Api.Features.Catalog;

public sealed record CatalogManifestResponse(
    string Version,
    int SchemaVersion,
    string SourceHash,
    IReadOnlyList<CatalogManifestDatasetResponse> Datasets
);

public sealed record CatalogManifestDatasetResponse(
    string Key,
    string Hash,
    string Url
);

public sealed record CatalogItemsResponse<TItem>(
    string Version,
    int SchemaVersion,
    string SourceHash,
    string DatasetKey,
    string DatasetHash,
    IReadOnlyList<TItem> Items
);
