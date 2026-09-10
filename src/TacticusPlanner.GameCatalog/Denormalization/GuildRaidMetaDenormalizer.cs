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
                    boss.Recommendations
                        .Select(recommendation => new GameCatalogGuildRaidMetaRecommendationView(
                            recommendation.Kind,
                            recommendation.HeroIds.ToArray(),
                            recommendation.MowId,
                            recommendation.CompIds.ToArray()))
                        .ToArray()))
                .ToArray());
}
