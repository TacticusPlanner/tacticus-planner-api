using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

/// <summary>Covers <c>fix-legendary-event-catalog-kill-points</c>'s <c>legendary-event-lane-points</c>
/// capability: a lane's <c>killPoints</c> is its defeat-all base (<c>defeatAll[0]</c>), and the loader rejects a
/// lane where the two disagree or <c>defeatAll</c> is empty.</summary>
public sealed class LreLanePointsTests
{
    private static readonly GameCatalogSnapshot Snapshot = GameCatalogLoader.Load();

    private static GameCatalogLre Raw(string id) => Snapshot.Lres.Single(lre => lre.UnitSnowprintId == id);

    [Theory]
    [InlineData("votanUthar", 30, 34, 43)]
    [InlineData("astarLysander", 32, 38, 31)]
    [InlineData("lostaBile", 37, 40, 46)]
    [InlineData("tauFarsight", 34, 26, 17)]
    public void ServedLanesCarryTheirDefeatAllBase(string id, int alpha, int beta, int gamma)
    {
        var view = Snapshot.LreViews.Single(lre => lre.Id == id);

        Assert.Equal((alpha, beta, gamma), (view.Alpha.KillPoints, view.Beta.KillPoints, view.Gamma.KillPoints));
        Assert.Equal(
            (alpha, beta, gamma),
            (view.Alpha.DefeatAll[0], view.Beta.DefeatAll[0], view.Gamma.DefeatAll[0]));
    }

    [Fact]
    public void EveryEmbeddedLaneHasKillPointsEqualToItsFirstDefeatAll()
    {
        Assert.NotEmpty(Snapshot.Lres);
        foreach (var lre in Snapshot.Lres)
        {
            foreach (var lane in new[] { lre.Alpha, lre.Beta, lre.Gamma })
            {
                Assert.Equal(lane.DefeatAll[0], lane.KillPoints);
            }
        }

        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateLreLanePoints(Snapshot.Lres, errors);
        Assert.Empty(errors);
    }

    [Fact]
    public void ACopiedKillPointsFailsNamingTheEventLaneAndBothValues()
    {
        var raw = Raw("votanUthar");
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateLreLanePoints([raw with { Alpha = raw.Alpha with { KillPoints = 34 } }], errors);

        var error = Assert.Single(errors);
        Assert.Equal((GameCatalogDatasets.LresPrefix, "InvalidLaneKillPoints"), (error.Dataset, error.Code));
        Assert.Contains("votanUthar", error.Message, StringComparison.Ordinal);
        Assert.Contains(raw.SourceFile, error.Message, StringComparison.Ordinal);
        Assert.Contains("Alpha", error.Message, StringComparison.Ordinal);
        Assert.Contains("killPoints 34", error.Message, StringComparison.Ordinal);
        Assert.Contains("defeatAll[0] 30", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AConsistentFilePasses()
    {
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateLreLanePoints([Raw("votanUthar")], errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void AnEmptyDefeatAllFailsNamingTheLane()
    {
        var raw = Raw("votanUthar");
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateLreLanePoints([raw with { Gamma = raw.Gamma with { DefeatAll = [] } }], errors);

        var error = Assert.Single(errors);
        Assert.Equal("InvalidLaneKillPoints", error.Code);
        Assert.Contains("Gamma", error.Message, StringComparison.Ordinal);
        Assert.Contains("defeatAll is empty", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCatalogValidatorRunsTheLanePointsCheck()
    {
        var raw = Raw("votanUthar");
        var broken = Snapshot.LresByEvent.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.UnitSnowprintId == "votanUthar" ? raw with { Alpha = raw.Alpha with { KillPoints = 34 } } : pair.Value);

        var errors = GameCatalogValidator.Validate(Snapshot with { LresByEvent = broken });

        Assert.Contains(errors, error => error.Code == "InvalidLaneKillPoints" && error.Message.Contains("votanUthar", StringComparison.Ordinal));
    }
}
