using System.Globalization;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private const double MaxLrePowerRatio = 10;
    private const double MaxLreUnitCoefficient = 5;

    private static void ValidateLreClearEstimate(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors)
    {
        var characterIds = new HashSet<string>(snapshot.Characters.Select(character => character.Id), StringComparer.Ordinal);

        ValidateLreClearEstimate(snapshot.LreClearEstimateRaw, characterIds, errors);
    }

    /// <summary>
    /// Validates the raw <c>lre-clear-estimate</c> file: exactly one record; <c>powerRatio</c> in (0, 10];
    /// <c>sampleCount</c> non-negative; <c>calibratedOn</c> a <c>yyyy-MM-dd</c> date when there are samples
    /// and null when there are none; every <c>unitId</c> a catalog character id (not a MoW or NPC) listed
    /// once; every <c>coefficient</c> in (0, 5]. Each failure is an <c>InvalidLreClearEstimate</c> error
    /// naming the offending value.
    /// </summary>
    internal static void ValidateLreClearEstimate(
        IReadOnlyList<GameCatalogLreClearEstimate> raw,
        HashSet<string> characterIds,
        List<GameCatalogValidationError> errors)
    {
        const string dataset = GameCatalogDatasets.LreClearEstimate;
        const string code = "InvalidLreClearEstimate";

        if (raw.Count != 1)
        {
            errors.Add(new GameCatalogValidationError(
                dataset, code, $"Dataset '{dataset}' must contain exactly one record, found {raw.Count}."));
            return;
        }

        var record = raw[0];

        if (record.Id != dataset)
        {
            errors.Add(new GameCatalogValidationError(
                dataset, code, $"Dataset '{dataset}' record id must be '{dataset}', found '{record.Id}'."));
        }

        if (!(record.PowerRatio > 0 && record.PowerRatio <= MaxLrePowerRatio))
        {
            errors.Add(new GameCatalogValidationError(
                dataset, code, $"Dataset '{dataset}' powerRatio '{FormatLreNumber(record.PowerRatio)}' must be in (0, {FormatLreNumber(MaxLrePowerRatio)}]."));
        }

        if (record.Calibration is null)
        {
            errors.Add(new GameCatalogValidationError(dataset, code, $"Dataset '{dataset}' is missing 'calibration'."));
        }
        else
        {
            ValidateLreClearEstimateCalibration(record.Calibration, errors);
        }

        var coefficients = record.UnitCoefficients ?? [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in coefficients)
        {
            if (string.IsNullOrWhiteSpace(unit.UnitId) || !characterIds.Contains(unit.UnitId))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, code, $"Dataset '{dataset}' unitId '{unit.UnitId}' is not a catalog character id."));
            }
            else if (!seen.Add(unit.UnitId))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, code, $"Dataset '{dataset}' unitId '{unit.UnitId}' is listed more than once."));
            }

            if (!(unit.Coefficient > 0 && unit.Coefficient <= MaxLreUnitCoefficient))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset,
                    code,
                    $"Dataset '{dataset}' unitId '{unit.UnitId}' coefficient '{FormatLreNumber(unit.Coefficient)}' must be in (0, {FormatLreNumber(MaxLreUnitCoefficient)}]."));
            }
        }
    }

    private static void ValidateLreClearEstimateCalibration(
        GameCatalogLreClearEstimateCalibration calibration,
        List<GameCatalogValidationError> errors)
    {
        const string dataset = GameCatalogDatasets.LreClearEstimate;
        const string code = "InvalidLreClearEstimate";

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

    private static string FormatLreNumber(double value) => value.ToString(CultureInfo.InvariantCulture);
}
