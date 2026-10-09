using FastEndpoints;
using FluentValidation;
using TacticusPlanner.Api.Features.Auth;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

public sealed class UpdateLegendaryEventPlanEndpoint : Endpoint<UpdateLegendaryEventPlanRequest, LegendaryEventPlanResponse>
{
    public override void Configure()
    {
        Put("me/legendary-event-plans/{eventId}");
        Summary(summary =>
        {
            summary.Summary = "Writes the plan-level fields (notes, paid options) of one Legendary Event plan.";
            summary.Description = "Creates the plan when it does not exist yet (expectedRevision 0). Every "
                + "successful write bumps the plan revision and answers with the whole plan.";
            summary.Response<LegendaryEventPlanResponse>(StatusCodes.Status200OK, "The plan after the write.");
            summary.Response(StatusCodes.Status400BadRequest, "Notes are too long.");
            summary.Response<LegendaryEventPlanConflictResponse>(StatusCodes.Status409Conflict,
                "expectedRevision is stale (issueCode legendaryEventPlanStale); the body carries the current plan.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The event is not a current catalog Legendary Event.");
        });
    }

    public override async Task HandleAsync(UpdateLegendaryEventPlanRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var eventId = Route<string>("eventId")!;
        if (Resolve<LegendaryEventCatalogValidator>().FindEvent(eventId) is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (await LegendaryEventPlanEndpointSupport.SendValidationFailuresAsync(
                this, LegendaryEventCatalogValidator.ValidateNotes(req.Notes), ct))
        {
            return;
        }

        var notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes;
        var result = await Resolve<LegendaryEventPlanWriter>().WriteAsync(profileId, eventId, req.ExpectedRevision, plan =>
        {
            plan.Notes = notes;
            plan.ShowPaidOptions = req.ShowPaidOptions;
            return LegendaryEventMutationOutcome.Changed;
        }, ct);
        await LegendaryEventPlanEndpointSupport.SendAsync(this, result, ct);
    }
}

public sealed record UpdateLegendaryEventPlanRequest(long ExpectedRevision, string? Notes, bool ShowPaidOptions);

public sealed class UpdateLegendaryEventPlanValidator : Validator<UpdateLegendaryEventPlanRequest>
{
    public UpdateLegendaryEventPlanValidator()
    {
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0);
    }
}
