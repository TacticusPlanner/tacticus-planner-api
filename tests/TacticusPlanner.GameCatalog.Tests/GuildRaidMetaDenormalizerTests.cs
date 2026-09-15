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
                    ["prime-2", "prime-1"],
                    [
                        new GameCatalogGuildRaidMetaRawRecommendation(
                            "rec-alternate",
                            "alternate",
                            [
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-5", "signature", true, []),
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-4", "core", true, ["hero-9", "hero-8"]),
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-3", "flex", false, []),
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-2", "flex", false, ["hero-7"]),
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-1", "flex", false, []),
                            ],
                            "mow-2",
                            ["mow-3", "mow-4"],
                            ["second", "first"],
                            1.86),
                        new GameCatalogGuildRaidMetaRawRecommendation(
                            "rec-meta",
                            "meta",
                            [
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-a", "signature", true, []),
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-b", "flex", false, []),
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-c", "flex", false, []),
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-d", "flex", false, []),
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-e", "flex", false, []),
                            ],
                            "mow-1",
                            [],
                            ["first"],
                            1.0),
                        new GameCatalogGuildRaidMetaRawRecommendation(
                            "rec-tertiary",
                            "tertiary",
                            [
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-f", "signature", true, []),
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-g", "flex", false, []),
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-h", "flex", false, []),
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-i", "flex", false, []),
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-j", "flex", false, []),
                            ],
                            "mow-1",
                            [],
                            ["first"],
                            1.14),
                    ]),
            ],
            [
                new GameCatalogGuildRaidMetaRawPrime(
                    "prime-2",
                    [
                        new GameCatalogGuildRaidMetaRawRecommendation(
                            "prime-rec-2",
                            "meta",
                            [
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-a", "signature", true, []),
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-b", "flex", false, []),
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-c", "flex", false, []),
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-d", "flex", false, []),
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-e", "flex", false, []),
                            ],
                            "mow-1",
                            [],
                            ["first"],
                            1.0),
                    ]),
                new GameCatalogGuildRaidMetaRawPrime(
                    "prime-1",
                    [
                        new GameCatalogGuildRaidMetaRawRecommendation(
                            "prime-rec-1",
                            "meta",
                            [
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-a", "signature", true, []),
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-b", "flex", false, []),
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-c", "flex", false, []),
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-d", "flex", false, []),
                                new GameCatalogGuildRaidMetaRawHeroSlot("hero-e", "flex", false, []),
                            ],
                            "mow-1",
                            [],
                            ["first"],
                            1.25),
                    ]),
            ]);

        var view = GameCatalogDenormalizer.BuildGuildRaidMeta(raw);

        Assert.Equal(["second", "first"], view.Comps.Select(comp => comp.Id));
        Assert.Equal(["core-2", "core-1"], view.Comps[0].CoreCharacterIds);
        Assert.Equal(["boss-2"], view.Bosses.Select(boss => boss.BossUnitSetId));

        // A boss with more than two recommendations preserves authored kind and efficiency order.
        Assert.Equal(["alternate", "meta", "tertiary"], view.Bosses[0].Recommendations.Select(recommendation => recommendation.Kind));
        Assert.Equal([1.86, 1.0, 1.14], view.Bosses[0].Recommendations.Select(recommendation => recommendation.Efficiency));

        // primeUnitSetIds on the boss group preserves authored order.
        Assert.Equal(["prime-2", "prime-1"], view.Bosses[0].PrimeUnitSetIds);

        // The top-level primes[] array preserves authored order, independent of the boss's own order.
        Assert.Equal(["prime-2", "prime-1"], view.Primes.Select(prime => prime.PrimeUnitSetId));
        Assert.Equal(["prime-rec-2"], view.Primes[0].Recommendations.Select(recommendation => recommendation.Id));
        Assert.Equal(1.0, view.Primes[0].Recommendations[0].Efficiency);
        Assert.Equal(1.25, view.Primes[1].Recommendations[0].Efficiency);

        var alternate = view.Bosses[0].Recommendations[0];
        Assert.Equal("rec-alternate", alternate.Id);
        Assert.Equal(["hero-5", "hero-4", "hero-3", "hero-2", "hero-1"], alternate.HeroSlots.Select(slot => slot.HeroId));
        Assert.Equal(["second", "first"], alternate.CompIds);
        Assert.Equal(["mow-3", "mow-4"], alternate.MowReplacementIds);

        // Populated replacement list is preserved unchanged, in authored order, without Comp inference.
        Assert.Equal(["hero-9", "hero-8"], alternate.HeroSlots[1].ReplacementCharacterIds);
        Assert.Equal("core", alternate.HeroSlots[1].RoleId);
        Assert.True(alternate.HeroSlots[1].Essential);

        // An authored empty replacement list stays empty rather than being filled from the Comp.
        Assert.Empty(alternate.HeroSlots[0].ReplacementCharacterIds);
        Assert.Empty(alternate.HeroSlots[2].ReplacementCharacterIds);

        var meta = view.Bosses[0].Recommendations[1];
        Assert.Equal("rec-meta", meta.Id);
        Assert.Empty(meta.MowReplacementIds);
    }
}
