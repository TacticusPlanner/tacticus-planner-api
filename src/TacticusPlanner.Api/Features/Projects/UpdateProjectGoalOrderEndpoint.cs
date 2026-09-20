using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>
/// Reorders a project's in-flight goals directly (plan: <c>add-inline-goal-reprioritize</c>) — replaces
/// the retired unit-keyed <c>PUT /me/projects/{id}/unit-order</c>. Priority is flat per-goal, not
/// unit-grouped: the caller submits the project's complete ordered set of in-flight goal ids, and it is
/// applied verbatim, with no dependency-based validation or reordering.
/// </summary>
public sealed class UpdateProjectGoalOrderEndpoint : Endpoint<UpdateProjectGoalOrderRequest, ProjectGoalsResponse>
{
    public override void Configure()
    {
        Put("me/projects/{projectId}/goal-order");
        Summary(summary =>
        {
            summary.Summary = "Reorders all in-flight goals in a project.";
            summary.Description = "GoalIds must be an exact, duplicate-free permutation of the project's "
                + "current in-flight (Active/Paused) goal ids. Accepts any ordering, including one that "
                + "places a goal ahead of a DependsOn prerequisite it hasn't reached.";
            summary.Response<ProjectGoalsResponse>(StatusCodes.Status200OK);
            summary.Response(StatusCodes.Status400BadRequest, "The request is not an exact permutation of the project's in-flight goals.");
            summary.Response(StatusCodes.Status404NotFound);
        });
    }

    public override async Task HandleAsync(UpdateProjectGoalOrderRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var projectId = ProjectId.From(Route<Guid>("projectId"));
        if (!await db.Projects.AnyAsync(project => project.Id == projectId, ct))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var planning = Resolve<ProjectGoalPlanningService>();
        await planning.ExecuteLockedMutationAsync([projectId], async transaction =>
        {
            var goalIds = req.GoalIds.Select(GoalId.From).ToList();
            if (!await planning.ApplyGoalOrderAsync(projectId, goalIds, ct))
            {
                AddError(request => request.GoalIds, "GoalIds must be an exact, duplicate-free permutation of the project's current in-flight goals.");
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
            }

            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
            var goals = await db.ProjectGoals.AsNoTracking()
                .Where(entry => entry.ProjectId == projectId)
                .OrderBy(entry => entry.Priority)
                .Select(entry => new ProjectGoalEntryResponse(entry.GoalId.Value, entry.Priority))
                .ToListAsync(ct);
            await Send.OkAsync(new ProjectGoalsResponse(goals), ct);
        }, ct);
    }
}

public sealed record UpdateProjectGoalOrderRequest(List<Guid> GoalIds);

public sealed class UpdateProjectGoalOrderValidator : Validator<UpdateProjectGoalOrderRequest>
{
    public UpdateProjectGoalOrderValidator()
    {
        RuleFor(request => request.GoalIds).NotNull();
    }
}
