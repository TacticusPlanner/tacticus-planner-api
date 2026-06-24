namespace TacticusPlanner.Api.Features.Catalog;

public sealed record CatalogManifestResponse(
    string Version,
    int SchemaVersion,
    string SourceHash,
    IReadOnlyList<CatalogManifestDatasetResponse> Datasets
);

public sealed record CatalogManifestDatasetResponse(
    string Key,
    string Hash
);

public sealed record CatalogItemsResponse<TItem>(
    string Version,
    string SourceHash,
    string DatasetHash,
    IReadOnlyList<TItem> Items
);
