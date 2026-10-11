namespace TacticusPlanner.GameCatalog.Models;

/// <summary>
/// The single <c>lre-clear-estimate</c> record: the power ratio and per-unit efficiency coefficients the
/// client uses to estimate how many battles of a Legendary Event lane a team (or a single unit) clears.
/// The raw <c>Data/lre-clear-estimate.json</c> file and the served record share this
/// shape; denormalization only sorts <see cref="UnitCoefficients"/> by unit id. A unit absent from
/// <see cref="UnitCoefficients"/> has coefficient 1.0, applied by the client (the server never expands it).
/// </summary>
public sealed record GameCatalogLreClearEstimate(
    string Id,
    double PowerRatio,
    GameCatalogLreClearEstimateCalibration Calibration,
    IReadOnlyList<GameCatalogLreUnitCoefficient> UnitCoefficients
);

/// <summary>
/// How the power ratio was calibrated. <see cref="SampleCount"/> 0 means uncalibrated, and then
/// <see cref="CalibratedOn"/> is null; otherwise it is an ISO-8601 calendar date (<c>yyyy-MM-dd</c>).
/// </summary>
public sealed record GameCatalogLreClearEstimateCalibration(
    int SampleCount,
    string? CalibratedOn
);

/// <summary>A curated per-unit efficiency coefficient (in (0, 5]; 1.0 is the baseline).</summary>
public sealed record GameCatalogLreUnitCoefficient(
    string UnitId,
    double Coefficient
);
