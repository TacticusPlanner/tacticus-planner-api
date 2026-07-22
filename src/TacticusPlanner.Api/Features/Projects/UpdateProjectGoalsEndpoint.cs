using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>
/// Replaces a project's goal membership and per-project priority ordering (plan §5) in one call. Every
/// goal must belong to at least one project, so a goal cannot be removed from its only remaining project —
/// such a removal is rejected wholesale (400) rather than silently orphaning the goal.
/// </summary>
public sealed class UpdateProjectGoalsEndpoint : Endpoint<UpdateProjectGoalsRequest, ProjectGoalsResponse>
{
    public override void Configure()
    {
        Put("me/projects/{projectId}/goals");
        Summary(summary =>
        {
            summary.Summary = "Replaces a project's goal membership and priority ordering.";
            summary.Response<ProjectGoalsResponse>(StatusCodes.Status200OK, "The project's updated goal membership.");
            summary.Response(StatusCodes.Status400BadRequest, "An unknown goal id, or a removal that would leave a goal in no project.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No matching project owned by the caller.");
        });
    }

    public override async Task HandleAsync(UpdateProjectGoalsRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var projectId = ProjectId.From(Route<Guid>("projectId"));
        var db = Resolve<PlannerDbContext>();

        // Both queries below are scoped to the caller's profile by PlannerDbContext's global query filter.
        var project = await db.Projects.FirstOrDefaultAsync(entity => entity.Id == projectId, ct);
        if (project is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var requestedGoalIds = req.Goals.Select(entry => GoalId.From(entry.GoalId)).ToHashSet();

        var ownedGoalIds = await db.Goals
            .Where(entity => requestedGoalIds.Contains(entity.Id))
            .Select(entity => entity.Id)
            .ToListAsync(ct);

        if (ownedGoalIds.Count != requestedGoalIds.Count)
        {
            AddError(request => request.Goals, "One or more goals do not exist or are not owned by the caller.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var existingMemberships = await db.ProjectGoals
            .Where(entity => entity.ProjectId == projectId)
            .ToListAsync(ct);

        var toRemove = existingMemberships.Where(entity => !requestedGoalIds.Contains(entity.GoalId)).ToList();
        if (toRemove.Count > 0)
        {
            var removedGoalIds = toRemove.Select(entity => entity.GoalId).ToHashSet();
            var otherMembershipCounts = await db.ProjectGoals
                .Where(entity => removedGoalIds.Contains(entity.GoalId) && entity.ProjectId != projectId)
                .GroupBy(entity => entity.GoalId)
                .Select(group => new { GoalId = group.Key, Count = group.Count() })
                .ToListAsync(ct);

            var orphaned = removedGoalIds
                .Except(otherMembershipCounts.Where(entry => entry.Count > 0).Select(entry => entry.GoalId))
                .ToList();

            if (orphaned.Count > 0)
            {
                AddError(request => request.Goals, "Cannot remove a goal from its only remaining project.");
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
            }

            db.ProjectGoals.RemoveRange(toRemove);
        }

        var existingByGoalId = existingMemberships.ToDictionary(entity => entity.GoalId);
        foreach (var entry in req.Goals)
        {
            var goalId = GoalId.From(entry.GoalId);
            if (existingByGoalId.TryGetValue(goalId, out var membership))
            {
                membership.Priority = entry.Priority;
            }
            else
            {
                db.ProjectGoals.Add(new ProjectGoal
                {
                    ProjectId = projectId,
                    GoalId = goalId,
                    Priority = entry.Priority,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        await db.SaveChangesAsync(ct);

        var updated = await db.ProjectGoals
            .AsNoTracking()
            .Where(entity => entity.ProjectId == projectId)
            .OrderBy(entity => entity.Priority)
            .Select(entity => new ProjectGoalEntryResponse(entity.GoalId.Value, entity.Priority))
            .ToListAsync(ct);

        await Send.OkAsync(new ProjectGoalsResponse(updated), ct);
    }
}

public sealed record UpdateProjectGoalsRequest(List<ProjectGoalEntryRequest> Goals);

public sealed record ProjectGoalEntryRequest(Guid GoalId, int Priority);

public sealed record ProjectGoalsResponse(List<ProjectGoalEntryResponse> Goals);

public sealed record ProjectGoalEntryResponse(Guid GoalId, int Priority);
