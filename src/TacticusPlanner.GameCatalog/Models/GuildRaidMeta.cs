namespace TacticusPlanner.GameCatalog.Models;

// ---- raw authored shape (internal to denormalization) ---------------------------------------------

/// <summary>
/// Curated Guild Raid strategy data. This source deliberately contains only stable game-data ids and
/// editorial evidence; presentation and source links remain client responsibilities.
/// </summary>
public sealed record GameCatalogGuildRaidMetaRawData(
    string SourceId,
    string UpdatedOn,
    IReadOnlyList<GameCatalogGuildRaidMetaRawComp> Comps,
    IReadOnlyList<GameCatalogGuildRaidMetaRawBoss> Bosses);

public sealed record GameCatalogGuildRaidMetaRawComp(
    string Id,
    string SignatureUnitId,
    IReadOnlyList<string> CoreCharacterIds,
    IReadOnlyList<string> FlexCharacterIds,
    IReadOnlyList<string> MowIds);

public sealed record GameCatalogGuildRaidMetaRawBoss(
    string BossUnitSetId,
    IReadOnlyList<GameCatalogGuildRaidMetaRawRecommendation> Recommendations);

public sealed record GameCatalogGuildRaidMetaRawRecommendation(
    string Kind,
    IReadOnlyList<string> HeroIds,
    string MowId,
    IReadOnlyList<string> CompIds,
    GameCatalogGuildRaidMetaRawEvidence? Evidence);

public sealed record GameCatalogGuildRaidMetaRawEvidence(
    int ReplayCount,
    int AverageDamage,
    int MaximumDamage);

// ---- served view (public catalog surface) -----------------------------------------------------------

/// <summary>
/// The id-only public <c>guild-raid-meta</c> dataset. The authored ordering of Comps, bosses,
/// recommendations, and unit ids is preserved so clients can render the curated guidance unchanged.
/// </summary>
public sealed record GameCatalogGuildRaidMetaView(
    string SourceId,
    string UpdatedOn,
    IReadOnlyList<GameCatalogGuildRaidMetaCompView> Comps,
    IReadOnlyList<GameCatalogGuildRaidMetaBossView> Bosses);

public sealed record GameCatalogGuildRaidMetaCompView(
    string Id,
    string SignatureUnitId,
    IReadOnlyList<string> CoreCharacterIds,
    IReadOnlyList<string> FlexCharacterIds,
    IReadOnlyList<string> MowIds);

public sealed record GameCatalogGuildRaidMetaBossView(
    string BossUnitSetId,
    IReadOnlyList<GameCatalogGuildRaidMetaRecommendationView> Recommendations);

public sealed record GameCatalogGuildRaidMetaRecommendationView(
    string Kind,
    IReadOnlyList<string> HeroIds,
    string MowId,
    IReadOnlyList<string> CompIds,
    GameCatalogGuildRaidMetaEvidenceView? Evidence);

public sealed record GameCatalogGuildRaidMetaEvidenceView(
    int ReplayCount,
    int AverageDamage,
    int MaximumDamage);
