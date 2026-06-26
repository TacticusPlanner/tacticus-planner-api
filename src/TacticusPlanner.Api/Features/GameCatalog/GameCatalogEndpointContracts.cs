namespace TacticusPlanner.Api.Features.GameCatalog;

/// <summary>
/// Envelope for one served (denormalized) dataset. Payload shape varies per entity. The manifest itself
/// is served as the domain <see cref="TacticusPlanner.GameCatalog.Models.GameCatalogManifest"/> (no
/// parallel API-side record).
/// </summary>
public sealed record GameCatalogDatasetEnvelope<TPayload>(
    string Version,
    int SchemaVersion,
    string GameVersion,
    string SourceHash,
    string DatasetKey,
    string DatasetHash,
    TPayload Data
);
