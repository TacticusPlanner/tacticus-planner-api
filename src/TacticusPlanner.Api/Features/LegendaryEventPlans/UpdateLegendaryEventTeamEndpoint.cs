using FastEndpoints;
using FluentValidation;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.LegendaryEvents;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

public sealed class UpdateLegendaryEventTeamEndpoint : Endpoint<UpdateLegendaryEventTeamRequest, LegendaryEventPlanResponse>
{
    public override void Configure()
    {
        Put("me/legendary-event-plans/{eventId}/teams/{teamId:guid}");
        Summary(summary =>
        {
            summary.Summary = "Replaces a team's name, members, reserve, objectives and one run's clear depth.";
            summary.Description = "The team's lane and position are untouched (a body carrying laneId is "
                + "rejected; delete and recreate to move a team). A non-null expectedBattleClears upserts the "
                + "given run's depth, a null one removes it; other runs' depths are left as they are.";
            summary.Response<LegendaryEventPlanResponse>(StatusCodes.Status200OK, "The plan after the write.");
            summary.Response(StatusCodes.Status400BadRequest, "A field is invalid for this event's catalog data; the error names it.");
            summary.Response<LegendaryEventPlanConflictResponse>(StatusCodes.Status409Conflict,
                "expectedRevision is stale (issueCode legendaryEventPlanStale); the body carries the current plan.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The event is not a current catalog Legendary Event, or the team is not on the caller's plan.");
        });
    }

    public override async Task HandleAsync(UpdateLegendaryEventTeamRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var eventId = Route<string>("eventId")!;
        var teamId = LegendaryEventTeamId.From(Route<Guid>("teamId"));
        var lre = Resolve<LegendaryEventCatalogValidator>().FindEvent(eventId);
        if (lre is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var depth = LegendaryEventPlanEndpointSupport.DepthOf(req.Run, req.ExpectedBattleClears, req.ExpectedBattleClearsSource);
        var content = LegendaryEventPlanEndpointSupport.ContentOf(req.Name ?? string.Empty, req.MemberUnitIds ?? [], req.ReserveUnitId, req.ObjectiveIndexes ?? []);
        var writer = Resolve<LegendaryEventPlanWriter>();
        IReadOnlyList<LegendaryEventValidationFailure> failures = [];
        var result = await writer.WriteAsync(profileId, eventId, req.ExpectedRevision, plan =>
        {
            var team = plan.Teams.FirstOrDefault(candidate => candidate.Id == teamId);
            if (team is null)
            {
                return LegendaryEventMutationOutcome.TeamNotFound;
            }

            // The lane is the team's own, so catalog checks run against it once the team is known.
            failures = LegendaryEventCatalogValidator.ValidateTeam(
                lre, team.LaneId, req.Name, req.MemberUnitIds, req.ReserveUnitId, req.ObjectiveIndexes, depth);
            if (failures.Count > 0)
            {
                return LegendaryEventMutationOutcome.Unchanged;
            }

            writer.ReplaceContent(team, content);
            LegendaryEventPlanWriter.ApplyRunDepth(team, depth, writer.Now);
            return LegendaryEventMutationOutcome.Changed;
        }, ct);

        if (await LegendaryEventPlanEndpointSupport.SendValidationFailuresAsync(this, failures, ct))
        {
            return;
        }

        await LegendaryEventPlanEndpointSupport.SendAsync(this, result, ct);
    }
}

/// <param name="LaneId">Must be absent: a team never changes lane.</param>
public sealed record UpdateLegendaryEventTeamRequest(
    long ExpectedRevision,
    string? Name,
    List<string>? MemberUnitIds,
    string? ReserveUnitId,
    List<int>? ObjectiveIndexes,
    int Run = LegendaryEventPlanRules.MinRun,
    int? ExpectedBattleClears = null,
    string? ExpectedBattleClearsSource = null,
    string? LaneId = null
);

public sealed class UpdateLegendaryEventTeamValidator : Validator<UpdateLegendaryEventTeamRequest>
{
    public UpdateLegendaryEventTeamValidator()
    {
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0);
        RuleFor(request => request.LaneId)
            .Null()
            .WithMessage("A team cannot change lane; delete it and create it on the other lane.");
        RuleFor(request => request.Name).NotEmpty();
        RuleFor(request => request.MemberUnitIds).NotNull();
        RuleFor(request => request.ObjectiveIndexes).NotNull();
        RuleFor(request => request.ExpectedBattleClearsSource)
            .Must(LegendaryEventPlanEndpointSupport.IsKnownSource)
            .WithMessage("The clear depth source must be estimate or manual.");
    }
}
