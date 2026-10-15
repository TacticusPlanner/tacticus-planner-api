namespace TacticusPlanner.GameCatalog.Models;

/// <summary>
/// The single <c>legendary-event-clear-estimate</c> record: the power ratio the client uses to estimate
/// how many battles of a Legendary Event lane a team (or a single unit) clears, and how it was calibrated.
/// The raw <c>Data/legendary-event-clear-estimate.json</c> file is served as-is. Per-character strength
/// comes from the generic <c>horde-mode-tiers</c> dataset (see <see cref="GameCatalogHordeModeTiers"/>).
/// </summary>
public sealed record GameCatalogLegendaryEventClearEstimate(
    string Id,
    double PowerRatio,
    GameCatalogLegendaryEventClearEstimateCalibration Calibration
);

/// <summary>
/// How the power ratio was calibrated. <see cref="SampleCount"/> 0 means uncalibrated, and then
/// <see cref="CalibratedOn"/> is null; otherwise it is an ISO-8601 calendar date (<c>yyyy-MM-dd</c>).
/// </summary>
public sealed record GameCatalogLegendaryEventClearEstimateCalibration(
    int SampleCount,
    string? CalibratedOn
);
