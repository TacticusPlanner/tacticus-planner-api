using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Replaces a goal's project membership from the goal side — the mirror of
/// <see cref="Projects.UpdateProjectGoalsEndpoint"/>, which replaces a project's membership from the
/// project side. Every goal must belong to at least one project (plan §5), so an empty list is rejected
/// outright rather than being allowed to orphan the goal.
/// </summary>
public sealed class UpdateGoalProjectsEndpoint : Endpoint<UpdateGoalProjectsRequest, GoalDetailResponse, GoalMapper>
{
    public override void Configure()
    {
        Put("me/goals/{goalId}/projects");
        Summary(summary =>
        {
            summary.Summary = "Replaces which projects a goal belongs to.";
            summary.Description = "A goal must always belong to at least one project, so an empty list is "
                + "rejected. Memberships in still-listed projects keep their existing priority; newly added "
                + "projects append the goal at the bottom of that project's ordering.";
            summary.Response<GoalDetailResponse>(StatusCodes.Status200OK, "The updated goal.");
            summary.Response(StatusCodes.Status400BadRequest, "An empty list, or an unknown project.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No matching goal owned by the caller.");
        });
    }

    public override async Task HandleAsync(UpdateGoalProjectsRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var goalId = GoalId.From(Route<Guid>("goalId"));
        var db = Resolve<PlannerDbContext>();
        var projectsService = Resolve<ProjectsService>();

        // Both queries below are scoped to the caller's profile by PlannerDbContext's global query filter.
        var goal = await db.Goals.FirstOrDefaultAsync(entity => entity.Id == goalId, ct);
        if (goal is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var requestedProjectIds = req.ProjectIds.Distinct().Select(ProjectId.From).ToHashSet();

        var ownedProjectIds = await db.Projects
            .Where(entity => requestedProjectIds.Contains(entity.Id))
            .Select(entity => entity.Id)
            .ToListAsync(ct);

        if (ownedProjectIds.Count != requestedProjectIds.Count)
        {
            AddError(request => request.ProjectIds, "Unknown project.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var existingMemberships = await db.ProjectGoals
            .Where(entity => entity.GoalId == goalId)
            .ToListAsync(ct);

        var toRemove = existingMemberships.Where(entity => !requestedProjectIds.Contains(entity.ProjectId)).ToList();
        if (toRemove.Count > 0)
        {
            db.ProjectGoals.RemoveRange(toRemove);
        }

        var existingByProjectId = existingMemberships.ToDictionary(entity => entity.ProjectId);
        foreach (var projectId in requestedProjectIds)
        {
            if (!existingByProjectId.ContainsKey(projectId))
            {
                db.ProjectGoals.Add(new ProjectGoal
                {
                    ProjectId = projectId,
                    GoalId = goalId,
                    Priority = await projectsService.GetNextPriorityAsync(projectId, ct),
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        await db.SaveChangesAsync(ct);

        var projectIds = requestedProjectIds.Select(id => id.Value).ToList();
        await Send.OkAsync(Map.ToDetail(goal, projectIds), ct);
    }
}

public sealed record UpdateGoalProjectsRequest(List<Guid> ProjectIds);

/// <summary>Request-shape rule only: the list must be non-empty. Project ownership (a DB lookup) stays a
/// handler-level check, mirroring <see cref="Projects.UpdateProjectGoalsEndpoint"/>'s goal-ownership check.</summary>
public sealed class UpdateGoalProjectsValidator : Validator<UpdateGoalProjectsRequest>
{
    public UpdateGoalProjectsValidator()
    {
        RuleFor(request => request.ProjectIds)
            .NotEmpty()
            .WithMessage("A goal must belong to at least one project.");
    }
}
