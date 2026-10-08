using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

public sealed class GetLegendaryEventPlanEndpoint : LegendaryEventPlanEndpointBase<EmptyRequest>
{
    public override void Configure()
    {
        Get(Route);
        Summary(summary =>
        {
            summary.Summary = "Reads the caller's plan for a Legendary Event.";
            summary.Description = "A missing plan reads as an empty plan at revision 0; nothing is created. Reads "
                + "never validate against the catalog, so a plan written under an older catalog still reads.";
            summary.Response<LegendaryEventPlanResponse>(StatusCodes.Status200OK);
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The event is not a current catalog Legendary Event.");
        });
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        if (await ResolveAsync(ct) is not { } resolved)
            return;

        var plan = await Resolve<PlannerDbContext>().LoadPlanAsync(resolved.Event.Id, tracked: false, ct);
        await Send.OkAsync(
            plan is null ? LegendaryEventPlanProjection.Empty(resolved.Event.Id) : LegendaryEventPlanProjection.Map(plan),
            ct);
    }
}

public sealed class UpdateLegendaryEventPlanEndpoint : LegendaryEventPlanEndpointBase<UpdateLegendaryEventPlanRequest>
{
    public override void Configure()
    {
        Put(Route);
        Summary(summary =>
        {
            summary.Summary = "Writes the plan-level fields (notes, showPaidOptions), creating the plan if needed.";
            DescribeWrite(summary);
        });
    }

    public override async Task HandleAsync(UpdateLegendaryEventPlanRequest req, CancellationToken ct)
    {
        if (await ResolveAsync(ct) is not { } resolved
            || await RejectAsync(LegendaryEventCatalogValidator.ValidatePlan(req.Notes), ct))
        {
            return;
        }

        var result = await Resolve<LegendaryEventPlanWriter>().WriteAsync(
            resolved.ProfileId, resolved.Event.Id, req.ExpectedRevision,
            (context, _) => Task.FromResult(LegendaryEventTeamMutations.UpdatePlan(context.Plan, req.Notes, req.ShowPaidOptions)),
            ct);
        await SendResultAsync(result, ct);
    }
}

public sealed class CreateLegendaryEventTeamEndpoint : LegendaryEventPlanEndpointBase<CreateLegendaryEventTeamRequest>
{
    public override void Configure()
    {
        Post($"{Route}/teams");
        Summary(summary =>
        {
            summary.Summary = "Creates a team on a lane, appended after the lane's existing teams.";
            summary.Description = "A non-null expectedBattleClears stores the depth for the given run only.";
            DescribeWrite(summary);
        });
    }

    public override async Task HandleAsync(CreateLegendaryEventTeamRequest req, CancellationToken ct)
    {
        var fields = new LegendaryEventTeamFields(
            req.Name, req.MemberUnitIds, req.ReserveUnitId, req.ObjectiveIndexes, req.Run,
            req.ExpectedBattleClears, req.ExpectedBattleClearsSource);
        if (await ResolveAsync(ct) is not { } resolved
            || await RejectAsync(LegendaryEventCatalogValidator.ValidateTeam(resolved.Event, req.LaneId, fields), ct))
        {
            return;
        }

        var result = await Resolve<LegendaryEventPlanWriter>().WriteAsync(
            resolved.ProfileId, resolved.Event.Id, req.ExpectedRevision,
            (context, _) =>
            {
                LegendaryEventTeamMutations.CreateTeam(context, req.LaneId, fields);
                return Task.FromResult(LegendaryEventPlanMutation.Applied);
            },
            ct);
        await SendResultAsync(result, ct);
    }
}

public sealed class UpdateLegendaryEventTeamEndpoint : LegendaryEventPlanEndpointBase<UpdateLegendaryEventTeamRequest>
{
    public override void Configure()
    {
        Put($"{Route}/teams/{{teamId}}");
        Summary(summary =>
        {
            summary.Summary = "Replaces a team's name, members, objectives and the given run's clear depth.";
            summary.Description = "The lane and order are kept (a body with laneId is rejected); a null "
                + "expectedBattleClears deletes only the given run's depth.";
            DescribeWrite(summary);
        });
    }

    public override async Task HandleAsync(UpdateLegendaryEventTeamRequest req, CancellationToken ct)
    {
        if (await ResolveAsync(ct) is not { } resolved)
            return;

        if (req.LaneId is not null)
        {
            await RejectAsync([new("laneId", "A team cannot change lane; delete it and create it on the other lane.")], ct);
            return;
        }

        var teamId = LegendaryEventTeamId.From(Route<Guid>("teamId"));
        var fields = new LegendaryEventTeamFields(
            req.Name, req.MemberUnitIds, req.ReserveUnitId, req.ObjectiveIndexes, req.Run,
            req.ExpectedBattleClears, req.ExpectedBattleClearsSource);

        // The lane comes from the stored team, so look it up before validating units and objectives against it.
        var db = Resolve<PlannerDbContext>();
        var laneId = await db.LegendaryEventTeams
            .Where(team => team.Id == teamId && team.Plan!.EventId == resolved.Event.Id)
            .Select(team => team.LaneId)
            .FirstOrDefaultAsync(ct);
        if (laneId is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (await RejectAsync(LegendaryEventCatalogValidator.ValidateTeam(resolved.Event, laneId, fields), ct))
            return;

        var result = await Resolve<LegendaryEventPlanWriter>().WriteAsync(
            resolved.ProfileId, resolved.Event.Id, req.ExpectedRevision,
            (context, token) => LegendaryEventTeamMutations.UpdateTeamAsync(context, teamId, fields, token),
            ct);
        await SendResultAsync(result, ct);
    }
}

public sealed class DeleteLegendaryEventTeamEndpoint : LegendaryEventPlanEndpointBase<DeleteLegendaryEventTeamRequest>
{
    public override void Configure()
    {
        Delete($"{Route}/teams/{{teamId}}");
        Summary(summary =>
        {
            summary.Summary = "Deletes a team and re-densifies its lane's order.";
            DescribeWrite(summary);
        });
    }

    public override async Task HandleAsync(DeleteLegendaryEventTeamRequest req, CancellationToken ct)
    {
        if (await ResolveAsync(ct) is not { } resolved)
            return;

        if (req.ExpectedRevision is not { } expectedRevision)
        {
            await RejectAsync([new("expectedRevision", "expectedRevision is required.")], ct);
            return;
        }

        var teamId = LegendaryEventTeamId.From(Route<Guid>("teamId"));
        var result = await Resolve<LegendaryEventPlanWriter>().WriteAsync(
            resolved.ProfileId, resolved.Event.Id, expectedRevision,
            (context, _) => Task.FromResult(LegendaryEventTeamMutations.DeleteTeam(context, teamId)),
            ct);
        await SendResultAsync(result, ct);
    }
}

public sealed class UpdateLegendaryEventTeamOrderEndpoint
    : LegendaryEventPlanEndpointBase<UpdateLegendaryEventTeamOrderRequest>
{
    public override void Configure()
    {
        Put($"{Route}/teams/order");
        Summary(summary =>
        {
            summary.Summary = "Replaces one lane's team order with the given complete id set.";
            summary.Description = "Sending the current order succeeds without bumping the revision.";
            DescribeWrite(summary);
        });
    }

    public override async Task HandleAsync(UpdateLegendaryEventTeamOrderRequest req, CancellationToken ct)
    {
        if (await ResolveAsync(ct) is not { } resolved)
            return;

        if (LegendaryEventCatalogValidator.FindLane(resolved.Event, req.LaneId) is null)
        {
            await RejectAsync([new("laneId", "laneId must be one of alpha, beta or gamma.")], ct);
            return;
        }

        var result = await Resolve<LegendaryEventPlanWriter>().WriteAsync(
            resolved.ProfileId, resolved.Event.Id, req.ExpectedRevision,
            (context, _) => Task.FromResult(LegendaryEventTeamMutations.ReorderLane(context.Plan, req.LaneId, req.TeamIds ?? [])),
            ct);
        await SendResultAsync(result, ct);
    }
}

public sealed class UpdateLegendaryEventPlanValidator : Validator<UpdateLegendaryEventPlanRequest>
{
    public UpdateLegendaryEventPlanValidator() =>
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0);
}

public sealed class CreateLegendaryEventTeamValidator : Validator<CreateLegendaryEventTeamRequest>
{
    public CreateLegendaryEventTeamValidator()
    {
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0);
        RuleFor(request => request.LaneId).NotEmpty();
        RuleFor(request => request.MemberUnitIds).NotNull();
        RuleFor(request => request.ObjectiveIndexes).NotNull();
    }
}

public sealed class UpdateLegendaryEventTeamValidator : Validator<UpdateLegendaryEventTeamRequest>
{
    public UpdateLegendaryEventTeamValidator()
    {
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0);
        RuleFor(request => request.LaneId).Null()
            .WithMessage("A team cannot change lane; delete it and create it on the other lane.");
        RuleFor(request => request.MemberUnitIds).NotNull();
        RuleFor(request => request.ObjectiveIndexes).NotNull();
    }
}

public sealed class UpdateLegendaryEventTeamOrderValidator : Validator<UpdateLegendaryEventTeamOrderRequest>
{
    public UpdateLegendaryEventTeamOrderValidator()
    {
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0);
        RuleFor(request => request.LaneId).NotEmpty();
        RuleFor(request => request.TeamIds).NotNull();
    }
}
