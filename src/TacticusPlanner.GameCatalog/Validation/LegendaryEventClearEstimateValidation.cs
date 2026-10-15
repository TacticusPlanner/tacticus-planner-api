using System.Globalization;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private const double MaxLegendaryEventPowerRatio = 10;

    private static void ValidateLegendaryEventClearEstimate(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors) =>
        ValidateLegendaryEventClearEstimate(snapshot.LegendaryEventClearEstimateViews, errors);

    /// <summary>
    /// Validates the <c>legendary-event-clear-estimate</c> file: exactly one record with the dataset's id;
    /// <c>powerRatio</c> in (0, 10]; <c>sampleCount</c> non-negative; <c>calibratedOn</c> a <c>yyyy-MM-dd</c>
    /// date when there are samples and null when there are none. Each failure is an
    /// <c>InvalidLegendaryEventClearEstimate</c> error naming the offending value.
    /// </summary>
    internal static void ValidateLegendaryEventClearEstimate(
        IReadOnlyList<GameCatalogLegendaryEventClearEstimate> records,
        List<GameCatalogValidationError> errors)
    {
        const string dataset = GameCatalogDatasets.LegendaryEventClearEstimate;
        const string code = "InvalidLegendaryEventClearEstimate";

        if (records.Count != 1)
        {
            errors.Add(new GameCatalogValidationError(
                dataset, code, $"Dataset '{dataset}' must contain exactly one record, found {records.Count}."));
            return;
        }

        var record = records[0];

        if (record.Id != dataset)
        {
            errors.Add(new GameCatalogValidationError(
                dataset, code, $"Dataset '{dataset}' record id must be '{dataset}', found '{record.Id}'."));
        }

        if (!(record.PowerRatio > 0 && record.PowerRatio <= MaxLegendaryEventPowerRatio))
        {
            errors.Add(new GameCatalogValidationError(
                dataset,
                code,
                $"Dataset '{dataset}' powerRatio '{FormatCatalogNumber(record.PowerRatio)}' must be in (0, {FormatCatalogNumber(MaxLegendaryEventPowerRatio)}]."));
        }

        if (record.Calibration is null)
        {
            errors.Add(new GameCatalogValidationError(dataset, code, $"Dataset '{dataset}' is missing 'calibration'."));
            return;
        }

        var calibration = record.Calibration;
        if (calibration.SampleCount < 0)
        {
            errors.Add(new GameCatalogValidationError(
                dataset, code, $"Dataset '{dataset}' sampleCount '{calibration.SampleCount}' must not be negative."));
        }

        if (calibration.SampleCount > 0)
        {
            if (!DateOnly.TryParseExact(calibration.CalibratedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset,
                    code,
                    $"Dataset '{dataset}' calibratedOn '{calibration.CalibratedOn}' must be an ISO-8601 calendar date when sampleCount is {calibration.SampleCount}."));
            }
        }
        else if (calibration.CalibratedOn is not null)
        {
            errors.Add(new GameCatalogValidationError(
                dataset,
                code,
                $"Dataset '{dataset}' calibratedOn '{calibration.CalibratedOn}' must be null when sampleCount is {calibration.SampleCount}."));
        }
    }

    private static string FormatCatalogNumber(double value) => value.ToString(CultureInfo.InvariantCulture);
}
