using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>
/// Replaces a project's goal membership in one call. Every goal must belong to at least one project, so a goal
/// cannot be removed from its only remaining project — such a removal is rejected wholesale (400
/// <c>lastProjectMembership</c>, naming the blocked goals). The request carries the membership the client reviewed
/// (<c>expectedGoalIds</c>); if it no longer matches the current membership under the lock the save is rejected as
/// stale (409 <c>membershipStale</c>). Membership is never slot-checked (in-flight uniqueness is per profile) and
/// never changes a goal's priority or status.
/// </summary>
public sealed class UpdateProjectGoalsEndpoint : Endpoint<UpdateProjectGoalsRequest, ProjectGoalsResponse>
{
    public const string MembershipStale = "membershipStale";
    public const string LastProjectMembership = "lastProjectMembership";

    public override void Configure()
    {
        Put("projects/{projectId}/goals");
        Summary(summary =>
        {
            summary.Summary = "Replaces a project's goal membership.";
            summary.Description = "Atomic: any rejection changes nothing. Never changes any goal's global priority, "
                + "status or the goal-order revision. expectedGoalIds is the membership the client reviewed; it is "
                + "compared with the current membership under the lock.";
            summary.Response<ProjectGoalsResponse>(StatusCodes.Status200OK,
                "The project's updated members in global-priority order.");
            summary.Response(StatusCodes.Status400BadRequest,
                "A removal that would leave goals in no project (issueCode lastProjectMembership, blockedGoalIds), "
                + "or an unknown goal id (validation).");
            summary.Response(StatusCodes.Status409Conflict,
                "membershipStale: expectedGoalIds no longer matches (current: { projectId, goalIds }).");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No matching project owned by the caller.");
        });
    }

    public override async Task HandleAsync(UpdateProjectGoalsRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var projectId = ProjectId.From(Route<Guid>("projectId"));
        var db = Resolve<PlannerDbContext>();

        // Both queries below are scoped to the caller's profile by PlannerDbContext's global query filter.
        if (!await db.Projects.AnyAsync(entity => entity.Id == projectId, ct))
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var requestedGoalIds = req.Goals.Select(entry => GoalId.From(entry.GoalId)).ToHashSet();
        var expectedGoalIds = req.ExpectedGoalIds.Select(GoalId.From).ToHashSet();
        await Resolve<ProjectGoalPlanningService>().ExecuteLockedMutationAsync(async transaction =>
        {
            // Read under the lock, so a concurrent membership change has either committed and is visible here,
            // or is still waiting for the lock.
            var existingMemberships = await db.ProjectGoals
                .Where(entity => entity.ProjectId == projectId)
                .ToListAsync(ct);
            if (!existingMemberships.Select(entity => entity.GoalId).ToHashSet().SetEquals(expectedGoalIds))
            {
                await this.SendConflictAsync(
                    MembershipStale,
                    "The project's goals changed after they were reviewed.",
                    new ProjectMembershipResponse(projectId.Value, existingMemberships.Select(entity => entity.GoalId.Value).ToList()),
                    ct);
                return;
            }

            if (await db.Goals.CountAsync(entity => requestedGoalIds.Contains(entity.Id), ct) != requestedGoalIds.Count)
            {
                AddError(request => request.Goals, "One or more goals do not exist or are not owned by the caller.");
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
            }

            var toRemove = existingMemberships.Where(entity => !requestedGoalIds.Contains(entity.GoalId)).ToList();
            if (toRemove.Count > 0)
            {
                var removedGoalIds = toRemove.Select(entity => entity.GoalId).ToList();
                var keptElsewhere = await db.ProjectGoals
                    .Where(entity => removedGoalIds.Contains(entity.GoalId) && entity.ProjectId != projectId)
                    .Select(entity => entity.GoalId)
                    .Distinct()
                    .ToListAsync(ct);
                var orphaned = removedGoalIds.Except(keptElsewhere).Select(id => id.Value).ToList();
                if (orphaned.Count > 0)
                {
                    const string message = "Cannot remove a goal from its only remaining project.";
                    await ApiProblems.WriteAsync(
                        HttpContext, StatusCodes.Status400BadRequest, LastProjectMembership, message,
                        extensions: new Dictionary<string, object?>
                        {
                            ["blockedGoalIds"] = orphaned,
                            ["errors"] = new Dictionary<string, string[]> { ["goals"] = [message] },
                        });
                    return;
                }

                db.ProjectGoals.RemoveRange(toRemove);
            }

            var now = Resolve<TimeProvider>().GetUtcNow();
            foreach (var goalId in requestedGoalIds.Where(id => existingMemberships.All(entity => entity.GoalId != id)))
                db.ProjectGoals.Add(ProjectGoalPlanningService.CreateMembership(projectId, goalId, now));

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            var updated = await db.ProjectGoals
                .AsNoTracking()
                .Where(entity => entity.ProjectId == projectId)
                .Join(db.Goals, entry => entry.GoalId, goal => goal.Id, (entry, goal) => goal)
                .OrderBy(goal => goal.GlobalPriority == null)
                .ThenBy(goal => goal.GlobalPriority)
                .ThenByDescending(goal => goal.CreatedAt)
                .ThenBy(goal => goal.Id)
                .Select(goal => new ProjectGoalEntryResponse(goal.Id.Value, goal.GlobalPriority))
                .ToListAsync(ct);

            await Send.OkAsync(new ProjectGoalsResponse(updated), ct);
        }, ct);
    }
}

/// <param name="Goals">The complete desired membership (goal ids only).</param>
/// <param name="ExpectedGoalIds">The complete membership the client reviewed; compared with the current
/// membership under the project lock.</param>
public sealed record UpdateProjectGoalsRequest(List<ProjectGoalEntryRequest> Goals, List<Guid> ExpectedGoalIds);

public sealed class UpdateProjectGoalsValidator : Validator<UpdateProjectGoalsRequest>
{
    public UpdateProjectGoalsValidator()
    {
        RuleFor(request => request.Goals).NotNull();
        RuleFor(request => request.ExpectedGoalIds).NotNull();
    }
}

/// <summary>The <c>current</c> of a <c>membershipStale</c> conflict: the project's whole membership as it stands.</summary>
public sealed record ProjectMembershipResponse(Guid ProjectId, List<Guid> GoalIds);

/// <summary>No <c>Priority</c> field (see the endpoint's class doc for why) — removed rather than kept
/// and ignored, since a field with no legitimate use is worse than no field.</summary>
public sealed record ProjectGoalEntryRequest(Guid GoalId);

public sealed record ProjectGoalsResponse(List<ProjectGoalEntryResponse> Goals);

/// <summary>A member goal with its account-wide position (null once completed/archived).</summary>
public sealed record ProjectGoalEntryResponse(Guid GoalId, int? GlobalPriority);
