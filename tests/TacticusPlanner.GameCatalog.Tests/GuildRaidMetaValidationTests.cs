using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class GuildRaidMetaValidationTests
{
    private static GameCatalogGuildRaidMetaRawHeroSlot Slot(
        string heroId, string roleId = "flex", bool essential = false, IReadOnlyList<string>? replacements = null) =>
        new(heroId, roleId, essential, replacements ?? []);

    private static readonly string[] DefaultHeroIds = ["hero-1", "hero-2", "hero-3", "hero-4", "hero-5"];

    private static GameCatalogGuildRaidMetaRawHeroSlot[] ValidSlots() =>
        DefaultHeroIds.Select(heroId => Slot(heroId)).ToArray();

    private static GameCatalogGuildRaidMetaRawRecommendation Recommendation(
        string id = "rec-1",
        string kind = "meta",
        IReadOnlyList<GameCatalogGuildRaidMetaRawHeroSlot>? heroSlots = null,
        string mowId = "mow-1",
        IReadOnlyList<string>? mowReplacementIds = null,
        IReadOnlyList<string>? compIds = null,
        double efficiency = 1.0) =>
        new(
            id,
            kind,
            heroSlots ?? ValidSlots(),
            mowId,
            mowReplacementIds ?? [],
            compIds ?? ["comp"],
            efficiency);

    private static GameCatalogGuildRaidMetaRawBoss Boss(
        string bossUnitSetId,
        IReadOnlyList<GameCatalogGuildRaidMetaRawRecommendation> recommendations,
        IReadOnlyList<string>? primeUnitSetIds = null) =>
        new(bossUnitSetId, primeUnitSetIds ?? [], recommendations);

    private static GameCatalogGuildRaidMetaRawData Data(
        string sourceId = "source",
        string updatedOn = "2026-07-01",
        IReadOnlyList<GameCatalogGuildRaidMetaRawComp>? comps = null,
        IReadOnlyList<GameCatalogGuildRaidMetaRawBoss>? bosses = null,
        IReadOnlyList<GameCatalogGuildRaidMetaRawPrime>? primes = null) =>
        new(
            sourceId,
            updatedOn,
            comps ?? [new GameCatalogGuildRaidMetaRawComp("comp", "hero-1", ["hero-1"], ["hero-2"], ["mow-1"])],
            bosses ?? [Boss("boss-1", [Recommendation()])],
            primes ?? []);

    private static List<GameCatalogValidationError> Validate(
        GameCatalogGuildRaidMetaRawData raw,
        HashSet<string>? characterIds = null,
        HashSet<string>? mowIds = null,
        HashSet<string>? bossIds = null,
        HashSet<string>? primeIds = null)
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateGuildRaidMeta(
            raw,
            GameCatalogDenormalizer.BuildGuildRaidMeta(raw),
            characterIds ?? new HashSet<string>(["hero-1", "hero-2", "hero-3", "hero-4", "hero-5"]),
            mowIds ?? new HashSet<string>(["mow-1"]),
            bossIds ?? new HashSet<string>(["boss-1"]),
            primeIds ?? new HashSet<string>(["prime-1"]),
            errors);
        return errors;
    }

    [Fact]
    public void ValidDataWithACharacterOrMachineOfWarSignaturePasses()
    {
        Assert.Empty(Validate(Data()));

        var mowSignature = Data(comps: [new GameCatalogGuildRaidMetaRawComp("comp", "mow-1", [], [], ["mow-1"])]);
        Assert.Empty(Validate(mowSignature));
    }

    [Fact]
    public void ValidDataWithPrimeGroupPasses()
    {
        var raw = Data(
            bosses: [Boss("boss-1", [Recommendation()], primeUnitSetIds: ["prime-1"])],
            primes: [new GameCatalogGuildRaidMetaRawPrime("prime-1", [Recommendation(id: "prime-rec-1")])]);

        Assert.Empty(Validate(raw));
    }

    [Fact]
    public void MoreThanTwoRecommendationsWithDistinctKindsPasses()
    {
        var raw = Data(
            bosses:
            [
                Boss(
                    "boss-1",
                    [
                        Recommendation(id: "rec-1", kind: "lavistodes"),
                        Recommendation(id: "rec-2", kind: "neuro"),
                        Recommendation(id: "rec-3", kind: "battlesuit"),
                    ]),
            ]);

        Assert.Empty(Validate(raw));
    }

    [Fact]
    public void SharedReplacementAcrossSlotsAndRecommendationsIsValid()
    {
        var raw = Data(
            bosses:
            [
                Boss(
                    "boss-1",
                    [
                        Recommendation(
                            id: "rec-1",
                            kind: "meta",
                            heroSlots:
                            [
                                Slot("hero-1", replacements: ["hero-6"]),
                                Slot("hero-2", replacements: ["hero-6"]),
                                Slot("hero-3"),
                                Slot("hero-4"),
                                Slot("hero-5"),
                            ]),
                        Recommendation(
                            id: "rec-2",
                            kind: "alternate",
                            heroSlots:
                            [
                                Slot("hero-1", replacements: ["hero-6"]),
                                Slot("hero-2"),
                                Slot("hero-3"),
                                Slot("hero-4"),
                                Slot("hero-5"),
                            ]),
                    ]),
            ]);

        var errors = Validate(raw, characterIds: new HashSet<string>(["hero-1", "hero-2", "hero-3", "hero-4", "hero-5", "hero-6"]));

        Assert.Empty(errors);
    }

    [Fact]
    public void InvalidShapeAndReferencesFailBeforeServing()
    {
        var raw = Data(
            sourceId: "",
            updatedOn: "July 2026",
            comps:
            [
                new GameCatalogGuildRaidMetaRawComp("comp", "missing-unit", ["hero-1", "hero-1"], ["missing-hero"], ["missing-mow"]),
                new GameCatalogGuildRaidMetaRawComp("comp", "hero-1", [], [], []),
            ],
            bosses:
            [
                Boss(
                    "prime-1",
                    [
                        Recommendation(
                            id: "",
                            kind: "unexpected",
                            heroSlots:
                            [
                                Slot("hero-1", roleId: ""),
                                Slot("wrong-hero-id"),
                                Slot("hero-2", replacements: ["hero-2", "hero-2"]),
                            ],
                            mowId: "missing-mow",
                            mowReplacementIds: ["missing-mow", "missing-mow"],
                            compIds: ["comp", "comp", "missing-comp"],
                            efficiency: 0),
                        Recommendation(
                            id: "rec-1",
                            kind: "unexpected",
                            heroSlots:
                            [
                                Slot("hero-1", replacements: ["hero-1"]),
                                Slot("hero-2"),
                                Slot("hero-3"),
                                Slot("hero-4"),
                                Slot("hero-5"),
                            ],
                            mowReplacementIds: ["mow-1"]),
                    ],
                    primeUnitSetIds: ["missing-prime"]),
            ]);

        var errors = Validate(raw);

        Assert.Contains(errors, error => error.Code == "RequiredField" && error.Message.Contains("sourceId"));
        Assert.Contains(errors, error => error.Code == "InvalidDate");
        Assert.Contains(errors, error => error.Code == "DuplicateId" && error.Message.Contains("comp id"));
        Assert.Contains(errors, error => error.Code == "MissingReference" && error.Message.Contains("signatureUnitId"));
        Assert.Contains(errors, error => error.Code == "MissingReference" && error.Message.Contains("flexCharacterIds"));
        Assert.Contains(errors, error => error.Code == "MissingReference" && error.Message.Contains("mowIds"));
        Assert.Contains(errors, error => error.Code == "MissingReference" && error.Message.Contains("served boss"));
        Assert.Contains(errors, error => error.Code == "MissingReference" && error.Message.Contains("primeUnitSetIds"));
        Assert.Contains(errors, error => error.Code == "InvalidEfficiency");

        // Recommendation identity.
        Assert.Contains(errors, error => error.Code == "RequiredField" && error.Message.Contains("'id'"));

        // Slot-level shape: empty roleId, misaligned heroId, duplicate replacement, self-repeated replacement.
        Assert.Contains(errors, error => error.Code == "RequiredField" && error.Message.Contains("roleId"));
        Assert.Contains(errors, error => error.Code == "MissingReference" && error.Message.Contains("heroId") && error.Message.Contains("wrong-hero-id"));
        Assert.Contains(errors, error => error.Code == "DuplicateId" && error.Message.Contains("replacementCharacterIds"));
        Assert.Contains(errors, error => error.Code == "SelfReplacement" && error.Message.Contains("heroId"));

        // Machine-of-War replacement list: duplicate entries and self-repeated mowId.
        Assert.Contains(errors, error => error.Code == "DuplicateId" && error.Message.Contains("mowReplacementIds"));
        Assert.Contains(errors, error => error.Code == "SelfReplacement" && error.Message.Contains("mowId"));
    }

    [Fact]
    public void NonPositiveEfficiencyFails()
    {
        var raw = Data(bosses: [Boss("boss-1", [Recommendation(efficiency: 0)])]);

        var errors = Validate(raw);

        Assert.Contains(errors, error => error.Code == "InvalidEfficiency");
    }

    [Fact]
    public void DuplicateKindWithinBossGroupFails()
    {
        var raw = Data(
            bosses:
            [
                Boss(
                    "boss-1",
                    [
                        Recommendation(id: "rec-1", kind: "lavistodes"),
                        Recommendation(id: "rec-2", kind: "lavistodes"),
                    ]),
            ]);

        var errors = Validate(raw);

        Assert.Contains(errors, error => error.Code == "DuplicateId" && error.Message.Contains("recommendation kind"));
    }

    [Fact]
    public void DuplicateKindWithinPrimeGroupFails()
    {
        var raw = Data(
            bosses: [Boss("boss-1", [Recommendation()], primeUnitSetIds: ["prime-1"])],
            primes:
            [
                new GameCatalogGuildRaidMetaRawPrime(
                    "prime-1",
                    [
                        Recommendation(id: "prime-rec-1", kind: "admech"),
                        Recommendation(id: "prime-rec-2", kind: "admech"),
                    ]),
            ]);

        var errors = Validate(raw);

        Assert.Contains(errors, error => error.Code == "DuplicateId" && error.Message.Contains("recommendation kind"));
    }

    [Fact]
    public void UnresolvedPrimeUnitSetIdFails()
    {
        var raw = Data(
            primes: [new GameCatalogGuildRaidMetaRawPrime("missing-prime", [Recommendation(id: "prime-rec-1")])]);

        var errors = Validate(raw);

        Assert.Contains(errors, error => error.Code == "MissingReference" && error.Message.Contains("served prime"));
    }

    [Fact]
    public void DuplicateRecommendationIdAcrossBossesFails()
    {
        var raw = Data(
            bosses:
            [
                Boss("boss-1", [Recommendation(id: "shared-id")]),
                Boss("boss-2", [Recommendation(id: "shared-id")]),
            ]);

        var errors = Validate(raw, bossIds: new HashSet<string>(["boss-1", "boss-2"]));

        Assert.Contains(errors, error => error.Code == "DuplicateId" && error.Message.Contains("recommendation id"));
    }

    [Fact]
    public void DuplicateRecommendationIdAcrossBossAndPrimeFails()
    {
        var raw = Data(
            bosses: [Boss("boss-1", [Recommendation(id: "shared-id")], primeUnitSetIds: ["prime-1"])],
            primes: [new GameCatalogGuildRaidMetaRawPrime("prime-1", [Recommendation(id: "shared-id")])]);

        var errors = Validate(raw);

        Assert.Contains(errors, error => error.Code == "DuplicateId" && error.Message.Contains("recommendation id"));
    }

    [Fact]
    public void UnresolvedReplacementReferencesFail()
    {
        var raw = Data(
            bosses:
            [
                Boss(
                    "boss-1",
                    [
                        Recommendation(
                            heroSlots:
                            [
                                Slot("hero-1", replacements: ["unknown-hero"]),
                                Slot("hero-2"),
                                Slot("hero-3"),
                                Slot("hero-4"),
                                Slot("hero-5"),
                            ],
                            mowReplacementIds: ["unknown-mow"]),
                    ]),
            ]);

        var errors = Validate(raw);

        Assert.Contains(errors, error =>
            error.Code == "MissingReference" && error.Message.Contains("replacementCharacterIds") && error.Message.Contains("unknown-hero"));
        Assert.Contains(errors, error =>
            error.Code == "MissingReference" && error.Message.Contains("mowReplacementIds") && error.Message.Contains("unknown-mow"));
    }

    [Fact]
    public void MissingHeroSlotsFails()
    {
        var raw = Data(
            bosses: [Boss("boss-1", [Recommendation(heroSlots: [Slot("hero-1"), Slot("hero-2")])])]);

        var errors = Validate(raw);

        Assert.Contains(errors, error => error.Code == "InvalidHeroSlotCount");
    }

    [Fact]
    public void DuplicateHeroIdAcrossSlotsFails()
    {
        var raw = Data(
            bosses:
            [
                Boss(
                    "boss-1",
                    [
                        Recommendation(
                            heroSlots:
                            [
                                Slot("hero-1"),
                                Slot("hero-1"),
                                Slot("hero-3"),
                                Slot("hero-4"),
                                Slot("hero-5"),
                            ]),
                    ]),
            ]);

        var errors = Validate(raw);

        Assert.Contains(errors, error => error.Code == "DuplicateId" && error.Message.Contains("heroId"));
    }
}
