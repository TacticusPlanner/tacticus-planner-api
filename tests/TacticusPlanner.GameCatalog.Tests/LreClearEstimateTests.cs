using System.Text.Json;
using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Utils;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class LreClearEstimateTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> CharacterIds =
        new(["bloodDante", "ultraCalgar", "ultraTigurius"], StringComparer.Ordinal);

    private static GameCatalogLreClearEstimate Record(
        double powerRatio = 1.0,
        int sampleCount = 0,
        string? calibratedOn = null,
        params GameCatalogLreUnitCoefficient[] coefficients) =>
        new(
            GameCatalogDatasets.LreClearEstimate,
            powerRatio,
            new GameCatalogLreClearEstimateCalibration(sampleCount, calibratedOn),
            coefficients);

    private static List<GameCatalogValidationError> Validate(params GameCatalogLreClearEstimate[] raw)
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateLreClearEstimate(raw, CharacterIds, errors);
        return errors;
    }

    private static void AssertSingleError(List<GameCatalogValidationError> errors, params string[] expectedFragments)
    {
        var error = Assert.Single(errors);
        Assert.Equal(GameCatalogDatasets.LreClearEstimate, error.Dataset);
        Assert.Equal("InvalidLreClearEstimate", error.Code);
        Assert.Contains("lre-clear-estimate", error.Message, StringComparison.Ordinal);
        foreach (var fragment in expectedFragments)
        {
            Assert.Contains(fragment, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void InitialDatasetLoadsUncalibratedWithNoCoefficients()
    {
        var snapshot = GameCatalogLoader.Load();

        var record = Assert.Single(snapshot.LreClearEstimateViews);
        Assert.Equal("lre-clear-estimate", record.Id);
        Assert.Equal(1.0, record.PowerRatio);
        Assert.Equal(0, record.Calibration.SampleCount);
        Assert.Null(record.Calibration.CalibratedOn);
        Assert.Empty(record.UnitCoefficients);
    }

    [Fact]
    public void DatasetIsListedInTheManifestWithAHash()
    {
        var snapshot = GameCatalogLoader.Load();

        var entry = Assert.Single(snapshot.Manifest.Datasets, dataset => dataset.Key == GameCatalogDatasets.LreClearEstimate);
        Assert.False(string.IsNullOrWhiteSpace(entry.Hash));
        Assert.Contains(GameCatalogDatasets.LreClearEstimate, GameCatalogDatasets.Served);
    }

    [Fact]
    public void CoefficientsAreSortedByUnitIdAndTheHashIgnoresAuthoringOrder()
    {
        var authored = Record(
            coefficients: [new GameCatalogLreUnitCoefficient("ultraTigurius", 1.2), new GameCatalogLreUnitCoefficient("bloodDante", 0.9)]);
        var reversed = Record(
            coefficients: [new GameCatalogLreUnitCoefficient("bloodDante", 0.9), new GameCatalogLreUnitCoefficient("ultraTigurius", 1.2)]);

        var served = GameCatalogDenormalizer.BuildLreClearEstimate([authored]);
        var servedReversed = GameCatalogDenormalizer.BuildLreClearEstimate([reversed]);

        Assert.Equal(["bloodDante", "ultraTigurius"], Assert.Single(served).UnitCoefficients.Select(unit => unit.UnitId));
        Assert.Equal(
            GameCatalogHashing.ComputeCanonicalJsonHash(served, JsonOptions),
            GameCatalogHashing.ComputeCanonicalJsonHash(servedReversed, JsonOptions));
    }

    [Fact]
    public void ValidRecordWithCoefficientsPasses()
    {
        Assert.Empty(Validate(Record(
            powerRatio: 0.5,
            sampleCount: 12,
            calibratedOn: "2026-10-12",
            new GameCatalogLreUnitCoefficient("ultraTigurius", 1.2),
            new GameCatalogLreUnitCoefficient("ultraCalgar", 5))));
    }

    [Fact]
    public void UnknownUnitFails() =>
        AssertSingleError(Validate(Record(coefficients: [new GameCatalogLreUnitCoefficient("notAUnit", 1.1)])), "notAUnit");

    [Fact]
    public void MachineOfWarIdFails()
    {
        // Validate against the real catalog roster: a MoW id is a unit, but not a character.
        var snapshot = GameCatalogLoader.Load();
        Assert.Contains(snapshot.Mows, mow => mow.Id == "necroReanimator");
        var characterIds = new HashSet<string>(snapshot.Characters.Select(character => character.Id), StringComparer.Ordinal);

        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateLreClearEstimate(
            [Record(coefficients: [new GameCatalogLreUnitCoefficient("necroReanimator", 1.1)])], characterIds, errors);

        AssertSingleError(errors, "necroReanimator");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5.1)]
    [InlineData(-1)]
    public void CoefficientOutOfBoundsFails(double coefficient) =>
        AssertSingleError(
            Validate(Record(coefficients: [new GameCatalogLreUnitCoefficient("ultraCalgar", coefficient)])),
            "ultraCalgar",
            $"'{coefficient.ToString(System.Globalization.CultureInfo.InvariantCulture)}'");

    [Theory]
    [InlineData(0)]
    [InlineData(10.5)]
    [InlineData(-0.5)]
    public void PowerRatioOutOfBoundsFails(double powerRatio) =>
        AssertSingleError(Validate(Record(powerRatio: powerRatio)), "powerRatio");

    [Fact]
    public void PowerRatioUpperBoundIsInclusive() => Assert.Empty(Validate(Record(powerRatio: 10)));

    [Fact]
    public void CalibrationDateWithoutSamplesFails() =>
        AssertSingleError(Validate(Record(sampleCount: 0, calibratedOn: "2026-10-12")), "calibratedOn", "2026-10-12");

    [Theory]
    [InlineData(null)]
    [InlineData("2026-13-01")]
    [InlineData("12/10/2026")]
    public void SamplesWithoutAValidCalibrationDateFail(string? calibratedOn) =>
        AssertSingleError(Validate(Record(sampleCount: 3, calibratedOn: calibratedOn)), "calibratedOn");

    [Fact]
    public void NegativeSampleCountFails() =>
        AssertSingleError(Validate(Record(sampleCount: -1)), "sampleCount", "-1");

    [Fact]
    public void DuplicateUnitFails() =>
        AssertSingleError(
            Validate(Record(coefficients:
            [
                new GameCatalogLreUnitCoefficient("ultraCalgar", 1.1),
                new GameCatalogLreUnitCoefficient("ultraCalgar", 0.9),
            ])),
            "ultraCalgar");

    [Fact]
    public void NotExactlyOneRecordFails()
    {
        AssertSingleError(Validate(), "exactly one record", "0");
        AssertSingleError(Validate(Record(), Record()), "exactly one record", "2");
    }
}
