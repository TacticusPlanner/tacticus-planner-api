using FastEndpoints;
using FluentValidation.Results;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

/// <summary>Shared plumbing for the <c>me/legendary-event-plans/{eventId}</c> endpoints: resolve the caller and
/// the catalog event (404 when either is missing), report field failures as 400, and answer a write result as
/// 200 / 404 / 409.</summary>
public abstract class LegendaryEventPlanEndpointBase<TRequest> : Endpoint<TRequest, LegendaryEventPlanResponse>
    where TRequest : notnull
{
    protected const string Route = "me/legendary-event-plans/{eventId}";

    protected async Task<(ProfileId ProfileId, GameCatalogLreView Event)?> ResolveAsync(CancellationToken ct)
    {
        var lre = Resolve<LegendaryEventCatalogValidator>().FindEvent(Route<string>("eventId")!);
        if (ProcessorState<CurrentUserState>().ProfileId is not { } profileId || lre is null)
        {
            await Send.NotFoundAsync(ct);
            return null;
        }

        return (profileId, lre);
    }

    protected async Task<bool> RejectAsync(IReadOnlyList<LegendaryEventFieldFailure> failures, CancellationToken ct)
    {
        if (failures.Count == 0)
            return false;

        foreach (var failure in failures)
            AddError(new ValidationFailure(failure.Field, failure.Message));
        await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
        return true;
    }

    protected async Task SendResultAsync(LegendaryEventPlanWriteResult result, CancellationToken ct)
    {
        switch (result)
        {
            case LegendaryEventPlanWriteResult.Saved saved:
                await Send.OkAsync(saved.Plan, ct);
                break;
            case LegendaryEventPlanWriteResult.Conflict conflict:
                HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                await HttpContext.Response.WriteAsJsonAsync(conflict.Body, ct);
                break;
            default:
                await Send.NotFoundAsync(ct);
                break;
        }
    }

    protected static void DescribeWrite(EndpointSummary summary)
    {
        summary.Response<LegendaryEventPlanResponse>(StatusCodes.Status200OK, "The whole plan after the write.");
        summary.Response(StatusCodes.Status400BadRequest, "A field fails validation against the current catalog; "
            + "the error names the field.");
        summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
        summary.Response(StatusCodes.Status404NotFound, "The event is not a current catalog Legendary Event, or the "
            + "team is not on this plan.");
        summary.Response<LegendaryEventPlanConflictResponse>(StatusCodes.Status409Conflict,
            "expectedRevision is stale or another write won (issueCode legendaryEventPlanStale), or a reorder's ids "
            + "are not the lane's team set (legendaryEventOrderSetMismatch). Carries the current plan.");
    }
}
