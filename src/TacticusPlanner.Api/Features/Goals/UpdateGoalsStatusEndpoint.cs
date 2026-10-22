using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Transitions one or many of the caller's goals to a lifecycle status in one atomic, profile-locked request
/// (<c>goal-bulk-status</c>): pause/resume keep the order position, completing/archiving releases it and reopening
/// a terminal goal appends it. Goals already in the status are left untouched. An unknown id, a slot conflict or a
/// stale order revision rejects the whole request.
/// </summary>
public sealed class UpdateGoalsStatusEndpoint : Endpoint<UpdateGoalsStatusRequest, UpdateGoalsStatusResponse, GoalMapper>
{
    public override void Configure()
    {
        Post("me/goals/status");
        Summary(summary =>
        {
            summary.Summary = "Transitions one or many goals to a status (Active/Paused/Completed/Archived), atomically.";
            summary.Description = "expectedOrderRevision is required when a goal enters or leaves the in-flight set "
                + "(to or from Completed/Archived) and ignored otherwise. Any rejection changes nothing.";
            summary.Response<UpdateGoalsStatusResponse>(StatusCodes.Status200OK,
                "Every named goal's detail, in request order, and the order revision after the transition.");
            summary.Response(StatusCodes.Status400BadRequest,
                "goalIds empty or repeated, an unknown status, or a missing expectedOrderRevision.");
            summary.Response(StatusCodes.Status409Conflict,
                "slotOccupied (goalId names the goal, current the goal holding its slot) or goalOrderStale "
                + "(current: { revision, goalIds }).");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "A goal id is not one of the caller's goals.");
        });
    }

    public override async Task HandleAsync(UpdateGoalsStatusRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var status = Enum.Parse<GoalStatus>(req.Status, ignoreCase: true);
        var ids = req.GoalIds.Select(GoalId.From).ToList();
        var db = Resolve<PlannerDbContext>();
        var planning = Resolve<ProjectGoalPlanningService>();
        var order = Resolve<GoalOrderService>();
        await planning.ExecuteLockedMutationAsync(async transaction =>
        {
            // Under the lock; scoped to the caller's profile by PlannerDbContext's global query filter. The goals
            // (tracked) and their memberships in one read.
            var rows = await db.Goals
                .Where(goal => ids.Contains(goal.Id))
                .Select(goal => new
                {
                    Goal = goal,
                    ProjectIds = db.ProjectGoals.Where(entry => entry.GoalId == goal.Id).Select(entry => entry.ProjectId).ToList(),
                })
                .ToListAsync(ct);
            var byId = rows.ToDictionary(row => row.Goal.Id, row => row.Goal);
            if (byId.Count != ids.Count)
            {
                await this.SendNotFoundProblemAsync(ct);
                return;
            }

            var goals = ids.Select(id => byId[id]).ToList();
            var changing = goals.Where(goal => goal.Status != status).ToList();
            var entering = ProjectGoalPlanningService.IsInFlight(status)
                ? changing.Where(goal => !ProjectGoalPlanningService.IsInFlight(goal.Status)).ToList()
                : [];
            var leaving = ProjectGoalPlanningService.IsInFlight(status)
                ? []
                : changing.Where(goal => ProjectGoalPlanningService.IsInFlight(goal.Status)).ToList();

            if (entering.Count > 0 || leaving.Count > 0)
            {
                if (req.ExpectedOrderRevision is not { } expected)
                {
                    AddError(request => request.ExpectedOrderRevision!,
                        "expectedOrderRevision is required when goals enter or leave the in-flight order.");
                    await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                    return;
                }

                // The profile row the lock loaded (tracked) holds the order revision as it stands.
                if (db.Profiles.Local.Single().GoalOrderRevision != expected)
                {
                    await GoalOrderResponses.SendAsync(
                        this, new GoalOrderResult(GoalOrderOutcome.StaleRevision, await order.ReadAsync(ct)), transaction: null, ct);
                    return;
                }
            }

            foreach (var goal in changing)
                goal.Status = status;
            if (await planning.FindConflictAsync(entering, ct) is { } conflict)
            {
                await GoalProblems.SendSlotConflictAsync(this, conflict, ct);
                return;
            }

            // Appends in request order.
            foreach (var goal in entering)
                await order.AppendAsync(goal, ct);
            foreach (var goal in leaving)
                order.Release(goal);

            await order.CompleteAsync(ct);
            await transaction.CommitAsync(ct);

            var projectIds = rows.ToDictionary(row => row.Goal.Id, row => row.ProjectIds.Select(id => id.Value).ToList());
            await Send.OkAsync(
                new UpdateGoalsStatusResponse(
                    goals.Select(goal => Map.ToDetail(goal, projectIds[goal.Id])).ToList(),
                    db.Profiles.Local.Single().GoalOrderRevision),
                ct);
        }, ct);
    }
}

/// <param name="GoalIds">The goals to transition: non-empty and distinct.</param>
/// <param name="Status">Active, Paused, Completed or Archived.</param>
/// <param name="ExpectedOrderRevision">The order revision the client read; required when the in-flight set
/// changes.</param>
public sealed record UpdateGoalsStatusRequest(List<Guid> GoalIds, string Status, long? ExpectedOrderRevision = null);

/// <param name="Goals">Every named goal's detail, in request order.</param>
/// <param name="OrderRevision">The account-wide order revision after the transition.</param>
public sealed record UpdateGoalsStatusResponse(List<GoalDetailResponse> Goals, long OrderRevision);

public sealed class UpdateGoalsStatusValidator : Validator<UpdateGoalsStatusRequest>
{
    public UpdateGoalsStatusValidator()
    {
        RuleFor(request => request.GoalIds)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("At least one goal is required.")
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("A goal appears more than once.");
        RuleFor(request => request.Status)
            .Must(value => Enum.TryParse<GoalStatus>(value, ignoreCase: true, out var status)
                && Enum.IsDefined(status)
                && !int.TryParse(value, out _))
            .WithMessage("Unknown or unsupported target status.");
        RuleFor(request => request.ExpectedOrderRevision)
            .GreaterThanOrEqualTo(0)
            .When(request => request.ExpectedOrderRevision is not null);
    }
}
