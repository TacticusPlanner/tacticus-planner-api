using FastEndpoints;
using FluentValidation.Results;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

/// <summary>Shared plumbing for the <c>legendary-event-plans/{eventId}</c> endpoints: resolve the caller and
/// the catalog event (404 when either is missing), report field failures as 400, and answer a write result as
/// 200 / 404 / 409.</summary>
public abstract class LegendaryEventPlanEndpointBase<TRequest> : Endpoint<TRequest, LegendaryEventPlanResponse>
    where TRequest : notnull
{
    protected const string Route = "legendary-event-plans/{eventId}";

    protected async Task<(ProfileId ProfileId, GameCatalogLreView Event)?> ResolveAsync(CancellationToken ct)
    {
        var lre = Resolve<LegendaryEventCatalogValidator>().FindEvent(Route<string>("eventId")!);
        if (ProcessorState<CurrentUserState>().ProfileId is not { } profileId || lre is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return null;
        }

        return (profileId, lre);
    }

    /// <summary>Read mode (LRE Stage 7, design D5): the caller and their stored plan, which stays readable after its
    /// event leaves the catalog. 404 when the caller has no profile, or the id is outside the catalog with no stored
    /// plan. A null plan means a current catalog event the caller never wrote.</summary>
    protected async Task<(string EventId, LegendaryEventPlan? Plan)?> ResolveReadAsync(CancellationToken ct)
    {
        var eventId = Route<string>("eventId")!;
        if (ProcessorState<CurrentUserState>().ProfileId is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return null;
        }

        var plan = await Resolve<PlannerDbContext>().LoadPlanAsync(eventId, tracked: false, ct);
        if (plan is null && Resolve<LegendaryEventCatalogValidator>().FindEvent(eventId) is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return null;
        }

        return (eventId, plan);
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
                await this.SendConflictAsync(conflict.IssueCode, conflict.Message, conflict.Current, ct);
                break;
            default:
                await this.SendNotFoundProblemAsync(ct);
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
        summary.Response(StatusCodes.Status409Conflict,
            "expectedRevision is stale or another write won (issueCode legendaryEventPlanStale), or a reorder's ids "
            + "are not the lane's team set (legendaryEventOrderSetMismatch). current is the plan as it stands.");
    }
}
