using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class GuildRaidMetaDenormalizerTests
{
    [Fact]
    public void PreservesAuthoredCompBossRecommendationAndUnitOrder()
    {
        var raw = new GameCatalogGuildRaidMetaRawData(
            "source",
            "2026-07-01",
            [
                new GameCatalogGuildRaidMetaRawComp(
                    "second", "character-2", ["core-2", "core-1"], ["flex-2"], ["mow-2"]),
                new GameCatalogGuildRaidMetaRawComp(
                    "first", "mow-1", [], ["flex-1"], ["mow-1"]),
            ],
            [
                new GameCatalogGuildRaidMetaRawBoss(
                    "boss-2",
                    [
                        new GameCatalogGuildRaidMetaRawRecommendation(
                            "alternate", ["hero-5", "hero-4", "hero-3", "hero-2", "hero-1"], "mow-2", ["second", "first"],
                            new GameCatalogGuildRaidMetaRawEvidence(4, 100, 120)),
                        new GameCatalogGuildRaidMetaRawRecommendation(
                            "meta", ["hero-a", "hero-b", "hero-c", "hero-d", "hero-e"], "mow-1", ["first"], null),
                    ]),
            ]);

        var view = GameCatalogDenormalizer.BuildGuildRaidMeta(raw);

        Assert.Equal(["second", "first"], view.Comps.Select(comp => comp.Id));
        Assert.Equal(["core-2", "core-1"], view.Comps[0].CoreCharacterIds);
        Assert.Equal(["boss-2"], view.Bosses.Select(boss => boss.BossUnitSetId));
        Assert.Equal(["alternate", "meta"], view.Bosses[0].Recommendations.Select(recommendation => recommendation.Kind));
        Assert.Equal(["hero-5", "hero-4", "hero-3", "hero-2", "hero-1"], view.Bosses[0].Recommendations[0].HeroIds);
        Assert.Equal(["second", "first"], view.Bosses[0].Recommendations[0].CompIds);
        Assert.Equal(new GameCatalogGuildRaidMetaEvidenceView(4, 100, 120), view.Bosses[0].Recommendations[0].Evidence);
        Assert.Null(view.Bosses[0].Recommendations[1].Evidence);
    }
}
