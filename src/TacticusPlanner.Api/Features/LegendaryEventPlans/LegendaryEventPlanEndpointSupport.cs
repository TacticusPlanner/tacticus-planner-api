using FastEndpoints;
using FluentValidation.Results;
using TacticusPlanner.Domain.LegendaryEvents;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

/// <summary>The response plumbing every plan endpoint shares: field-named 400s for validation failures and
/// the 200/404/409 mapping of a <see cref="LegendaryEventPlanWriteResult"/>.</summary>
internal static class LegendaryEventPlanEndpointSupport
{
    public const string StaleIssueCode = "legendaryEventPlanStale";
    public const string OrderSetMismatchIssueCode = "legendaryEventOrderSetMismatch";

    public static async Task<bool> SendValidationFailuresAsync(
        IEndpoint endpoint,
        IReadOnlyList<LegendaryEventValidationFailure> failures,
        CancellationToken ct)
    {
        if (failures.Count == 0)
        {
            return false;
        }

        foreach (var failure in failures)
        {
            endpoint.ValidationFailures.Add(new ValidationFailure(failure.Field, failure.Message));
        }

        endpoint.HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await endpoint.HttpContext.Response.WriteAsJsonAsync(
            new ErrorResponse(endpoint.ValidationFailures, StatusCodes.Status400BadRequest), ct);
        return true;
    }

    public static async Task SendAsync(IEndpoint endpoint, LegendaryEventPlanWriteResult result, CancellationToken ct)
    {
        var response = endpoint.HttpContext.Response;
        switch (result)
        {
            case LegendaryEventPlanWriteResult.Ok ok:
                await response.WriteAsJsonAsync(ok.Plan, ct);
                break;
            case LegendaryEventPlanWriteResult.Stale stale:
                response.StatusCode = StatusCodes.Status409Conflict;
                await response.WriteAsJsonAsync(new LegendaryEventPlanConflictResponse(
                    StaleIssueCode,
                    "The plan changed since it was loaded. Reload it and try again.",
                    stale.Plan), ct);
                break;
            case LegendaryEventPlanWriteResult.OrderSetMismatch mismatch:
                response.StatusCode = StatusCodes.Status409Conflict;
                await response.WriteAsJsonAsync(new LegendaryEventPlanConflictResponse(
                    OrderSetMismatchIssueCode,
                    "The team ids do not match the lane's current teams.",
                    mismatch.Plan), ct);
                break;
            default:
                await response.SendNotFoundAsync(ct);
                break;
        }
    }

    /// <summary>The request's depth fields as one write, with the wire source already parsed; an unknown
    /// source string is reported by the endpoint's validator before this runs.</summary>
    public static LegendaryEventRunDepthWrite DepthOf(int run, int? expectedBattleClears, string? source) =>
        new(run, expectedBattleClears, LegendaryEventDepthSources.FromWire(source));

    public static LegendaryEventTeamContent ContentOf(
        string name, IReadOnlyList<string> memberUnitIds, string? reserveUnitId, IReadOnlyList<int> objectiveIndexes) =>
        new(name.Trim(), memberUnitIds, string.IsNullOrWhiteSpace(reserveUnitId) ? null : reserveUnitId, objectiveIndexes);

    public static bool IsKnownSource(string? source) =>
        source is null || LegendaryEventDepthSources.FromWire(source) is not null;

    public static string LaneList => string.Join(", ", LegendaryEventPlanRules.LaneIds);
}
