namespace TacticusPlanner.GameCatalog.Models;

/// <summary>
/// The active catalog manifest: release metadata plus the per-dataset hashes and download urls. This is
/// the single manifest contract — both the domain snapshot and the manifest endpoint serve this shape
/// (no parallel API-side record).
/// </summary>
public sealed record GameCatalogManifest(
    string Version,
    int SchemaVersion,
    string GameVersion,
    string SourceHash,
    IReadOnlyList<GameCatalogManifestDataset> Datasets
);

public sealed record GameCatalogManifestDataset(
    string Key,
    string Hash,
    string Url
);
