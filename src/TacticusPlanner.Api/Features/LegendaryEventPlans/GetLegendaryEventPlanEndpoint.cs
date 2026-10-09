using FastEndpoints;
using TacticusPlanner.Api.Features.Auth;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

public sealed class GetLegendaryEventPlanEndpoint : EndpointWithoutRequest<LegendaryEventPlanResponse>
{
    public override void Configure()
    {
        Get("me/legendary-event-plans/{eventId}");
        Summary(summary =>
        {
            summary.Summary = "Gets the caller's plan for one Legendary Event.";
            summary.Description = "A profile that has no plan for the event reads an empty plan at revision 0 "
                + "(no row is created). Reads never fail on catalog drift: stored ids are served as they are, "
                + "with the catalogVersion the plan was last written under.";
            summary.Response<LegendaryEventPlanResponse>(StatusCodes.Status200OK, "The plan.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The event is not a current catalog Legendary Event, or the profile is not provisioned.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is null)
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

        await Send.OkAsync(await Resolve<LegendaryEventPlanProjection>().ReadAsync(eventId, ct), ct);
    }
}
