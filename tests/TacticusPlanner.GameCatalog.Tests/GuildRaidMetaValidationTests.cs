using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class GuildRaidMetaValidationTests
{
    private static GameCatalogGuildRaidMetaRawData Data(
        string sourceId = "source",
        string updatedOn = "2026-07-01",
        IReadOnlyList<GameCatalogGuildRaidMetaRawComp>? comps = null,
        IReadOnlyList<GameCatalogGuildRaidMetaRawBoss>? bosses = null) =>
        new(
            sourceId,
            updatedOn,
            comps ?? [new GameCatalogGuildRaidMetaRawComp("comp", "hero-1", ["hero-1"], ["hero-2"], ["mow-1"])],
            bosses ??
            [
                new GameCatalogGuildRaidMetaRawBoss(
                    "boss-1",
                    [new GameCatalogGuildRaidMetaRawRecommendation(
                        "meta", ["hero-1", "hero-2", "hero-3", "hero-4", "hero-5"], "mow-1", ["comp"],
                        new GameCatalogGuildRaidMetaRawEvidence(4, 100, 120))]),
            ]);

    private static List<GameCatalogValidationError> Validate(GameCatalogGuildRaidMetaRawData raw)
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateGuildRaidMeta(
            raw,
            GameCatalogDenormalizer.BuildGuildRaidMeta(raw),
            new HashSet<string>(["hero-1", "hero-2", "hero-3", "hero-4", "hero-5"]),
            new HashSet<string>(["mow-1"]),
            new HashSet<string>(["boss-1"]),
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
                new GameCatalogGuildRaidMetaRawBoss(
                    "prime-1",
                    [
                        new GameCatalogGuildRaidMetaRawRecommendation(
                            "unexpected", ["hero-1", "hero-1", "hero-2"], "missing-mow", ["comp", "comp", "missing-comp"],
                            new GameCatalogGuildRaidMetaRawEvidence(-1, -1, -1)),
                        new GameCatalogGuildRaidMetaRawRecommendation(
                            "unexpected", ["hero-1", "hero-2", "hero-3", "hero-4", "hero-5"], "mow-1", ["comp"], null),
                    ]),
            ]);

        var errors = Validate(raw);

        Assert.Contains(errors, error => error.Code == "RequiredField" && error.Message.Contains("sourceId"));
        Assert.Contains(errors, error => error.Code == "InvalidDate");
        Assert.Contains(errors, error => error.Code == "DuplicateId" && error.Message.Contains("comp id"));
        Assert.Contains(errors, error => error.Code == "MissingReference" && error.Message.Contains("signatureUnitId"));
        Assert.Contains(errors, error => error.Code == "MissingReference" && error.Message.Contains("flexCharacterIds"));
        Assert.Contains(errors, error => error.Code == "MissingReference" && error.Message.Contains("mowIds"));
        Assert.Contains(errors, error => error.Code == "MissingReference" && error.Message.Contains("served boss"));
        Assert.Contains(errors, error => error.Code == "InvalidRecommendationKind");
        Assert.Contains(errors, error => error.Code == "InvalidHeroCount");
        Assert.Contains(errors, error => error.Code == "InvalidEvidence");
    }
}
