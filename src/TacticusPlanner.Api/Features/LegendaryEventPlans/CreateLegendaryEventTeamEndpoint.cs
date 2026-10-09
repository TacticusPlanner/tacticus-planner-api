using FastEndpoints;
using FluentValidation;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.LegendaryEvents;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

public sealed class CreateLegendaryEventTeamEndpoint : Endpoint<CreateLegendaryEventTeamRequest, LegendaryEventPlanResponse>
{
    public override void Configure()
    {
        Post("me/legendary-event-plans/{eventId}/teams");
        Summary(summary =>
        {
            summary.Summary = "Adds a team to one lane of a Legendary Event plan.";
            summary.Description = "The team is appended after the lane's existing teams. Members, the reserve, "
                + "objective indexes, run and clear depth are validated against the current catalog. A non-null "
                + "expectedBattleClears stores that run's depth; the plan is created when it does not exist yet "
                + "(expectedRevision 0).";
            summary.Response<LegendaryEventPlanResponse>(StatusCodes.Status200OK, "The plan with the new team.");
            summary.Response(StatusCodes.Status400BadRequest, "A field is invalid for this event's catalog data; the error names it.");
            summary.Response<LegendaryEventPlanConflictResponse>(StatusCodes.Status409Conflict,
                "expectedRevision is stale (issueCode legendaryEventPlanStale); the body carries the current plan.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The event is not a current catalog Legendary Event.");
        });
    }

    public override async Task HandleAsync(CreateLegendaryEventTeamRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var eventId = Route<string>("eventId")!;
        var lre = Resolve<LegendaryEventCatalogValidator>().FindEvent(eventId);
        if (lre is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var depth = LegendaryEventPlanEndpointSupport.DepthOf(req.Run, req.ExpectedBattleClears, req.ExpectedBattleClearsSource);
        var failures = LegendaryEventCatalogValidator.ValidateTeam(
            lre, req.LaneId, req.Name, req.MemberUnitIds, req.ReserveUnitId, req.ObjectiveIndexes, depth);
        if (await LegendaryEventPlanEndpointSupport.SendValidationFailuresAsync(this, failures, ct))
        {
            return;
        }

        var content = LegendaryEventPlanEndpointSupport.ContentOf(req.Name!, req.MemberUnitIds!, req.ReserveUnitId, req.ObjectiveIndexes!);
        var writer = Resolve<LegendaryEventPlanWriter>();
        var result = await writer.WriteAsync(profileId, eventId, req.ExpectedRevision, plan =>
        {
            LegendaryEventPlanWriter.AppendTeam(plan, req.LaneId!, content, depth, writer.Now);
            return LegendaryEventMutationOutcome.Changed;
        }, ct);
        await LegendaryEventPlanEndpointSupport.SendAsync(this, result, ct);
    }
}

/// <param name="Run">The event run (1–3) the depth belongs to; the client takes it from its synced
/// <c>currentEventRun</c>, defaulting to 1.</param>
/// <param name="ExpectedBattleClears">Null stores no depth for the run.</param>
/// <param name="ExpectedBattleClearsSource"><c>estimate</c> or <c>manual</c>; required with a depth.</param>
public sealed record CreateLegendaryEventTeamRequest(
    long ExpectedRevision,
    string? LaneId,
    string? Name,
    List<string>? MemberUnitIds,
    string? ReserveUnitId,
    List<int>? ObjectiveIndexes,
    int Run = LegendaryEventPlanRules.MinRun,
    int? ExpectedBattleClears = null,
    string? ExpectedBattleClearsSource = null
);

public sealed class CreateLegendaryEventTeamValidator : Validator<CreateLegendaryEventTeamRequest>
{
    public CreateLegendaryEventTeamValidator()
    {
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0);
        RuleFor(request => request.LaneId)
            .Must(LegendaryEventPlanRules.IsLane)
            .WithMessage($"The lane must be one of {LegendaryEventPlanEndpointSupport.LaneList}.");
        RuleFor(request => request.Name).NotEmpty();
        RuleFor(request => request.MemberUnitIds).NotNull();
        RuleFor(request => request.ObjectiveIndexes).NotNull();
        RuleFor(request => request.ExpectedBattleClearsSource)
            .Must(LegendaryEventPlanEndpointSupport.IsKnownSource)
            .WithMessage("The clear depth source must be estimate or manual.");
    }
}
