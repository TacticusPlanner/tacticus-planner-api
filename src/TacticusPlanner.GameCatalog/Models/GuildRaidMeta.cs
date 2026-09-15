namespace TacticusPlanner.GameCatalog.Models;

// ---- raw authored shape (internal to denormalization) ---------------------------------------------

/// <summary>
/// Curated Guild Raid strategy data. This source deliberately contains only stable game-data ids and
/// curated team guidance; presentation and source links remain client responsibilities.
/// </summary>
public sealed record GameCatalogGuildRaidMetaRawData(
    string SourceId,
    string UpdatedOn,
    IReadOnlyList<GameCatalogGuildRaidMetaRawComp> Comps,
    IReadOnlyList<GameCatalogGuildRaidMetaRawBoss> Bosses,
    IReadOnlyList<GameCatalogGuildRaidMetaRawPrime> Primes);

/// <summary>The raw <c>Data/guild-raid-meta/guild-raid-comps.json</c> shape: Comp guidance only.</summary>
public sealed record GameCatalogGuildRaidMetaRawCompsFile(
    string SourceId,
    string UpdatedOn,
    IReadOnlyList<GameCatalogGuildRaidMetaRawComp> Comps);

public sealed record GameCatalogGuildRaidMetaRawComp(
    string Id,
    string SignatureUnitId,
    IReadOnlyList<string> CoreCharacterIds,
    IReadOnlyList<string> FlexCharacterIds,
    IReadOnlyList<string> MowIds);

/// <summary>
/// The raw per-boss <c>Data/guild-raid-meta/guild-raid-meta-boss-{n}-{Slug}.json</c> shape: that boss's
/// own recommendations plus the recommendations for any prime fought alongside it. The loader splits this
/// into one <see cref="GameCatalogGuildRaidMetaRawBoss"/> and zero or more flattened
/// <see cref="GameCatalogGuildRaidMetaRawPrime"/> entries.
/// </summary>
public sealed record GameCatalogGuildRaidMetaRawBossFile(
    string BossUnitSetId,
    IReadOnlyList<string> PrimeUnitSetIds,
    IReadOnlyList<GameCatalogGuildRaidMetaRawRecommendation> Recommendations,
    IReadOnlyList<GameCatalogGuildRaidMetaRawPrime>? Primes);

public sealed record GameCatalogGuildRaidMetaRawBoss(
    string BossUnitSetId,
    IReadOnlyList<string> PrimeUnitSetIds,
    IReadOnlyList<GameCatalogGuildRaidMetaRawRecommendation> Recommendations);

public sealed record GameCatalogGuildRaidMetaRawPrime(
    string PrimeUnitSetId,
    IReadOnlyList<GameCatalogGuildRaidMetaRawRecommendation> Recommendations);

public sealed record GameCatalogGuildRaidMetaRawRecommendation(
    string Id,
    string Kind,
    IReadOnlyList<GameCatalogGuildRaidMetaRawHeroSlot> HeroSlots,
    string MowId,
    IReadOnlyList<string> MowReplacementIds,
    IReadOnlyList<string> CompIds,
    double Efficiency);

public sealed record GameCatalogGuildRaidMetaRawHeroSlot(
    string HeroId,
    string RoleId,
    bool Essential,
    IReadOnlyList<string> ReplacementCharacterIds);

// ---- served view (public catalog surface) -----------------------------------------------------------

/// <summary>
/// The id-only public <c>guild-raid-meta</c> dataset. The authored ordering of Comps, bosses,
/// recommendations, and unit ids is preserved so clients can render the curated guidance unchanged.
/// </summary>
public sealed record GameCatalogGuildRaidMetaView(
    string SourceId,
    string UpdatedOn,
    IReadOnlyList<GameCatalogGuildRaidMetaCompView> Comps,
    IReadOnlyList<GameCatalogGuildRaidMetaBossView> Bosses,
    IReadOnlyList<GameCatalogGuildRaidMetaPrimeView> Primes);

public sealed record GameCatalogGuildRaidMetaCompView(
    string Id,
    string SignatureUnitId,
    IReadOnlyList<string> CoreCharacterIds,
    IReadOnlyList<string> FlexCharacterIds,
    IReadOnlyList<string> MowIds);

public sealed record GameCatalogGuildRaidMetaBossView(
    string BossUnitSetId,
    IReadOnlyList<string> PrimeUnitSetIds,
    IReadOnlyList<GameCatalogGuildRaidMetaRecommendationView> Recommendations);

public sealed record GameCatalogGuildRaidMetaPrimeView(
    string PrimeUnitSetId,
    IReadOnlyList<GameCatalogGuildRaidMetaRecommendationView> Recommendations);

public sealed record GameCatalogGuildRaidMetaRecommendationView(
    string Id,
    string Kind,
    IReadOnlyList<GameCatalogGuildRaidMetaHeroSlotView> HeroSlots,
    string MowId,
    IReadOnlyList<string> MowReplacementIds,
    IReadOnlyList<string> CompIds,
    double Efficiency);

public sealed record GameCatalogGuildRaidMetaHeroSlotView(
    string HeroId,
    string RoleId,
    bool Essential,
    IReadOnlyList<string> ReplacementCharacterIds);
