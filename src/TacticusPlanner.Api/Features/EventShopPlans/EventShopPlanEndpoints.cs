using FastEndpoints;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.EventShopPlans;

internal static class EventShopPlanRoutes
{
    public const string Plans = "event-shop-plans";

    public const string Plan = "event-shop-plans/{eventId}";

    public static void DescribeAuth(EndpointSummary summary)
    {
        summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
        summary.Response(StatusCodes.Status404NotFound, "The caller has no profile.");
    }

    public static void DescribeConflict(EndpointSummary summary) =>
        summary.Response(StatusCodes.Status409Conflict,
            "expectedRevision is stale or another write won (issueCode eventShopPlanStale); current is the plan as it stands.");

    public static Task SendConflictAsync(IEndpoint endpoint, EventShopPlanResponse current, CancellationToken ct) =>
        endpoint.SendConflictAsync(
            EventShopPlanIssueCodes.Stale,
            "The plan changed since it was loaded. Review the current plan and try again.",
            current,
            ct);
}

public sealed class ListEventShopPlansEndpoint : EndpointWithoutRequest<EventShopPlanListResponse>
{
    public override void Configure()
    {
        Get(EventShopPlanRoutes.Plans);
        Summary(summary =>
        {
            summary.Summary = "Lists every stored event shop plan of the caller's profile.";
            summary.Description = "Ordered by eventId (ordinal). Events never written are not listed; plans whose event "
                + "left the catalog are included.";
            summary.Response<EventShopPlanListResponse>(StatusCodes.Status200OK);
            EventShopPlanRoutes.DescribeAuth(summary);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        // Scoped to the caller's profile by PlannerDbContext's global query filter.
        var plans = await Resolve<PlannerDbContext>().EventShopPlans.AsNoTracking().ToListAsync(ct);
        var validator = Resolve<EventShopPlanCatalogValidator>();
        await Send.OkAsync(
            new EventShopPlanListResponse(plans
                .OrderBy(plan => plan.EventId, StringComparer.Ordinal)
                .Select(plan => EventShopPlanProjection.Map(plan, validator.FindEvent(plan.EventId)))
                .ToArray()),
            ct);
    }
}

public sealed class GetEventShopPlanEndpoint : Endpoint<EmptyRequest, EventShopPlanResponse>
{
    public override void Configure()
    {
        Get(EventShopPlanRoutes.Plan);
        Summary(summary =>
        {
            summary.Summary = "Reads the caller's plan for an event shop.";
            summary.Description = "A missing plan reads as revision 0 with no entries, whether or not the event is in the "
                + "catalog; nothing is created. A stored plan stays readable after its event leaves the catalog.";
            summary.Response<EventShopPlanResponse>(StatusCodes.Status200OK);
            EventShopPlanRoutes.DescribeAuth(summary);
        });
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var eventId = Route<string>("eventId")!;
        var plan = await Resolve<PlannerDbContext>().LoadEventShopPlanAsync(eventId, tracked: false, ct);
        await Send.OkAsync(
            plan is null
                ? EventShopPlanProjection.Empty(eventId)
                : EventShopPlanProjection.Map(plan, Resolve<EventShopPlanCatalogValidator>().FindEvent(eventId)),
            ct);
    }
}

public sealed class PutEventShopPlanEndpoint : Endpoint<PutEventShopPlanRequest, EventShopPlanResponse>
{
    public override void Configure()
    {
        Put(EventShopPlanRoutes.Plan);
        Summary(summary =>
        {
            summary.Summary = "Replaces the cart of the caller's event shop plan, creating the plan if needed.";
            summary.Description = "Each entry must name a week, day and slot of the current catalog event, a reward that "
                + "slot offers that day, a quantity within that offer's per-day cap and a purchased quantity up to it. "
                + "Lock ids and power levels are not checked. A cart equal to the stored one succeeds without bumping "
                + "the revision.";
            summary.Response<EventShopPlanResponse>(StatusCodes.Status200OK, "The whole plan after the write.");
            summary.Response(StatusCodes.Status400BadRequest, "An entry fails validation; the error names the field.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound,
                "The event is not in the current catalog, or the caller has no profile.");
            EventShopPlanRoutes.DescribeConflict(summary);
        });
    }

    public override async Task HandleAsync(PutEventShopPlanRequest req, CancellationToken ct)
    {
        var eventShop = Resolve<EventShopPlanCatalogValidator>().FindEvent(Route<string>("eventId")!);
        if (ProcessorState<CurrentUserState>().ProfileId is not { } profileId || eventShop is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var failures = EventShopPlanCatalogValidator.ValidateEntries(eventShop, req.Entries);
        if (failures.Count > 0)
        {
            foreach (var failure in failures)
                AddError(new ValidationFailure(failure.Field, failure.Message));
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var result = await Resolve<EventShopPlanWriter>().ReplaceEntriesAsync(
            profileId,
            eventShop,
            req.ExpectedRevision,
            req.Entries!.Select(EventShopPlanProjection.ToEntity).ToArray(),
            ct);
        switch (result)
        {
            case EventShopPlanWriteResult.Saved saved:
                await Send.OkAsync(saved.Plan, ct);
                break;
            case EventShopPlanWriteResult.Conflict conflict:
                await EventShopPlanRoutes.SendConflictAsync(this, conflict.Current, ct);
                break;
        }
    }
}

public sealed class DeleteEventShopPlanEndpoint : Endpoint<DeleteEventShopPlanRequest>
{
    public override void Configure()
    {
        Delete(EventShopPlanRoutes.Plan);
        Summary(summary =>
        {
            summary.Summary = "Deletes the caller's plan for an event shop.";
            summary.Description = "expectedRevision is required; 0 with no stored plan succeeds. Works whether or not the "
                + "event is still in the catalog.";
            summary.Response(StatusCodes.Status204NoContent, "The plan is gone.");
            summary.Response(StatusCodes.Status400BadRequest, "expectedRevision is missing.");
            EventShopPlanRoutes.DescribeAuth(summary);
            EventShopPlanRoutes.DescribeConflict(summary);
        });
    }

    public override async Task HandleAsync(DeleteEventShopPlanRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        if (req.ExpectedRevision is not { } expectedRevision)
        {
            AddError(new ValidationFailure("expectedRevision", "expectedRevision is required."));
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var eventId = Route<string>("eventId")!;
        var result = await Resolve<EventShopPlanWriter>().DeleteAsync(
            eventId, Resolve<EventShopPlanCatalogValidator>().FindEvent(eventId), expectedRevision, ct);
        if (result is EventShopPlanWriteResult.Conflict conflict)
        {
            await EventShopPlanRoutes.SendConflictAsync(this, conflict.Current, ct);
            return;
        }

        await Send.NoContentAsync(ct);
    }
}
