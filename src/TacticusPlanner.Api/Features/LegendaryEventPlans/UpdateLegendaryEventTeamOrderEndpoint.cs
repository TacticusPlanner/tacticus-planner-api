using FastEndpoints;
using FluentValidation;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.LegendaryEvents;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

public sealed class UpdateLegendaryEventTeamOrderEndpoint : Endpoint<UpdateLegendaryEventTeamOrderRequest, LegendaryEventPlanResponse>
{
    public override void Configure()
    {
        Put("me/legendary-event-plans/{eventId}/teams/order");
        Summary(summary =>
        {
            summary.Summary = "Replaces the order of one lane's teams.";
            summary.Description = "teamIds must be the complete, duplicate-free set of that lane's team ids in "
                + "the desired order; dense 0-based sortOrder is assigned in that order. An order that is "
                + "already current succeeds without bumping the revision.";
            summary.Response<LegendaryEventPlanResponse>(StatusCodes.Status200OK, "The plan after the reorder.");
            summary.Response(StatusCodes.Status400BadRequest, "The lane id is not alpha, beta or gamma.");
            summary.Response<LegendaryEventPlanConflictResponse>(StatusCodes.Status409Conflict,
                "expectedRevision is stale (legendaryEventPlanStale), or teamIds is not the lane's exact team set "
                + "(legendaryEventOrderSetMismatch); the body carries the current plan either way.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The event is not a current catalog Legendary Event.");
        });
    }

    public override async Task HandleAsync(UpdateLegendaryEventTeamOrderRequest req, CancellationToken ct)
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

        var teamIds = req.TeamIds!.Select(LegendaryEventTeamId.From).ToList();
        var result = await Resolve<LegendaryEventPlanWriter>().WriteAsync(
            profileId, eventId, req.ExpectedRevision, plan => LegendaryEventPlanWriter.Reorder(plan, req.LaneId!, teamIds), ct);
        await LegendaryEventPlanEndpointSupport.SendAsync(this, result, ct);
    }
}

public sealed record UpdateLegendaryEventTeamOrderRequest(long ExpectedRevision, string? LaneId, List<Guid>? TeamIds);

public sealed class UpdateLegendaryEventTeamOrderValidator : Validator<UpdateLegendaryEventTeamOrderRequest>
{
    public UpdateLegendaryEventTeamOrderValidator()
    {
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0);
        RuleFor(request => request.LaneId)
            .Must(LegendaryEventPlanRules.IsLane)
            .WithMessage($"The lane must be one of {LegendaryEventPlanEndpointSupport.LaneList}.");
        RuleFor(request => request.TeamIds).NotNull();
    }
}
