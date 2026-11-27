using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>Builds the id-only Guild Raid Meta payload without joining presentation data.</summary>
    public static GameCatalogGuildRaidMetaView BuildGuildRaidMeta(GameCatalogGuildRaidMetaRawData raw) =>
        new(
            raw.SourceId,
            raw.UpdatedOn,
            raw.Comps
                .Select(comp => new GameCatalogGuildRaidMetaCompView(
                    comp.Id,
                    comp.SignatureUnitId,
                    comp.CoreCharacterIds.ToArray(),
                    comp.FlexCharacterIds.ToArray(),
                    comp.MowIds.ToArray()))
                .ToArray(),
            raw.Bosses
                .Select(boss => new GameCatalogGuildRaidMetaBossView(
                    boss.BossUnitSetId,
                    boss.PrimeUnitSetIds.ToArray(),
                    boss.Recommendations.Select(BuildGuildRaidMetaRecommendation).ToArray()))
                .ToArray(),
            raw.Primes
                .Select(prime => new GameCatalogGuildRaidMetaPrimeView(
                    prime.PrimeUnitSetId,
                    prime.Recommendations.Select(BuildGuildRaidMetaRecommendation).ToArray()))
                .ToArray());

    private static GameCatalogGuildRaidMetaRecommendationView BuildGuildRaidMetaRecommendation(
        GameCatalogGuildRaidMetaRawRecommendation recommendation) =>
        new(
            recommendation.Id,
            recommendation.Kind,
            recommendation.HeroSlots
                .Select(slot => new GameCatalogGuildRaidMetaHeroSlotView(
                    slot.HeroId,
                    slot.RoleId,
                    slot.Essential,
                    slot.ReplacementCharacterIds.ToArray()))
                .ToArray(),
            recommendation.MowId,
            recommendation.MowReplacementIds.ToArray(),
            recommendation.CompIds.ToArray(),
            recommendation.Efficiency);
}
