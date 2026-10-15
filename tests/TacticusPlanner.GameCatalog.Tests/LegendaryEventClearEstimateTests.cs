using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class LegendaryEventClearEstimateTests
{
    private static GameCatalogLegendaryEventClearEstimate Record(
        double powerRatio = 1.0,
        int sampleCount = 0,
        string? calibratedOn = null) =>
        new(
            GameCatalogDatasets.LegendaryEventClearEstimate,
            powerRatio,
            new GameCatalogLegendaryEventClearEstimateCalibration(sampleCount, calibratedOn));

    private static List<GameCatalogValidationError> Validate(params GameCatalogLegendaryEventClearEstimate[] records)
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateLegendaryEventClearEstimate(records, errors);
        return errors;
    }

    private static void AssertSingleError(List<GameCatalogValidationError> errors, params string[] expectedFragments)
    {
        var error = Assert.Single(errors);
        Assert.Equal(GameCatalogDatasets.LegendaryEventClearEstimate, error.Dataset);
        Assert.Equal("InvalidLegendaryEventClearEstimate", error.Code);
        Assert.Contains("legendary-event-clear-estimate", error.Message, StringComparison.Ordinal);
        foreach (var fragment in expectedFragments)
        {
            Assert.Contains(fragment, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void InitialDatasetLoadsUncalibrated()
    {
        var snapshot = GameCatalogLoader.Load();

        var record = Assert.Single(snapshot.LegendaryEventClearEstimateViews);
        Assert.Equal("legendary-event-clear-estimate", record.Id);
        Assert.Equal(1.0, record.PowerRatio);
        Assert.Equal(0, record.Calibration.SampleCount);
        Assert.Null(record.Calibration.CalibratedOn);
    }

    [Fact]
    public void DatasetIsListedInTheManifestWithAHash()
    {
        var snapshot = GameCatalogLoader.Load();

        var entry = Assert.Single(snapshot.Manifest.Datasets, dataset => dataset.Key == GameCatalogDatasets.LegendaryEventClearEstimate);
        Assert.False(string.IsNullOrWhiteSpace(entry.Hash));
        Assert.Contains(GameCatalogDatasets.LegendaryEventClearEstimate, GameCatalogDatasets.Served);
    }

    [Fact]
    public void CalibratedRecordPasses() =>
        Assert.Empty(Validate(Record(powerRatio: 0.5, sampleCount: 12, calibratedOn: "2026-10-12")));

    [Fact]
    public void WrongRecordIdFails() =>
        AssertSingleError(Validate(Record() with { Id = "not-the-id" }), "not-the-id");

    [Fact]
    public void MissingCalibrationFails() =>
        AssertSingleError(Validate(Record() with { Calibration = null! }), "calibration");

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
    public void NotExactlyOneRecordFails()
    {
        AssertSingleError(Validate(), "exactly one record", "0");
        AssertSingleError(Validate(Record(), Record()), "exactly one record", "2");
    }
}
