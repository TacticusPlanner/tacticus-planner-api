using FastEndpoints;
using FluentValidation;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.LegendaryEvents;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

public sealed class DeleteLegendaryEventTeamEndpoint : Endpoint<DeleteLegendaryEventTeamRequest, LegendaryEventPlanResponse>
{
    public override void Configure()
    {
        Delete("me/legendary-event-plans/{eventId}/teams/{teamId:guid}");
        Summary(summary =>
        {
            summary.Summary = "Removes a team from a Legendary Event plan and re-densifies its lane's order.";
            summary.Response<LegendaryEventPlanResponse>(StatusCodes.Status200OK, "The plan after the removal.");
            summary.Response<LegendaryEventPlanConflictResponse>(StatusCodes.Status409Conflict,
                "expectedRevision is stale (issueCode legendaryEventPlanStale); the body carries the current plan.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The event is not a current catalog Legendary Event, or the team is not on the caller's plan.");
        });
    }

    public override async Task HandleAsync(DeleteLegendaryEventTeamRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var eventId = Route<string>("eventId")!;
        var teamId = LegendaryEventTeamId.From(Route<Guid>("teamId"));
        if (Resolve<LegendaryEventCatalogValidator>().FindEvent(eventId) is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var result = await Resolve<LegendaryEventPlanWriter>().WriteAsync(
            profileId, eventId, req.ExpectedRevision, plan => LegendaryEventPlanWriter.RemoveTeam(plan, teamId), ct);
        await LegendaryEventPlanEndpointSupport.SendAsync(this, result, ct);
    }
}

/// <param name="ExpectedRevision">Query string: the plan revision the client last loaded.</param>
public sealed record DeleteLegendaryEventTeamRequest([property: QueryParam] long ExpectedRevision);

public sealed class DeleteLegendaryEventTeamValidator : Validator<DeleteLegendaryEventTeamRequest>
{
    public DeleteLegendaryEventTeamValidator()
    {
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0);
    }
}
