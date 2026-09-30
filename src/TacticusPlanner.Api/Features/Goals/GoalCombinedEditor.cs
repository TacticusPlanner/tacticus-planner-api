using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <param name="Goal">The goal as edited (null only for <see cref="GoalEditResult.NotFound"/>).</param>
/// <param name="ProjectIds">The goal's project memberships after the edit.</param>
/// <param name="Order">The order after the move; null unless the request carried <c>priority</c>.</param>
public sealed record GoalEditOutcome(
    GoalEditResult Result, Goal? Goal, List<ProjectId> ProjectIds, GoalOrderSnapshot? Order);

/// <summary>
/// The all-or-nothing combined edit behind <see cref="EditGoalEndpoint"/> (<c>goal-combined-edit</c>): the
/// target, details, projects and priority cores run in that order inside one
/// <see cref="ProjectGoalPlanningService.ExecuteLockedMutationAsync"/> delegate, with one
/// <c>SaveChangesAsync</c> and one commit. Any non-applied result rolls the transaction back. It writes no
/// HTTP response: the caller maps the returned outcome once the delegate has returned (the execution
/// strategy may not replay a delegate that already wrote one).
/// </summary>
public sealed class GoalCombinedEditor(
    PlannerDbContext db,
    ProjectGoalPlanningService planning,
    GoalTargetEditor targetEditor,
    GoalDetailsEditor detailsEditor,
    GoalMembershipEditor membershipEditor,
    GoalOrderService orderService)
{
    public async Task<GoalEditOutcome> ApplyAsync(
        ProfileId profileId, GoalId goalId, EditGoalRequest req, CancellationToken ct)
    {
        // Scoped to the caller's profile by PlannerDbContext's global query filter.
        var goal = await db.Goals.FirstOrDefaultAsync(entity => entity.Id == goalId, ct);
        if (goal is null)
            return Failed(new GoalEditResult.NotFound());

        var requestedProjectIds = req.ProjectIds?.Distinct().Select(ProjectId.From).ToHashSet();
        var ownedProjects = new List<Project>();
        if (requestedProjectIds is not null)
        {
            ownedProjects = await db.Projects.Where(entity => requestedProjectIds.Contains(entity.Id)).ToListAsync(ct);
            if (ownedProjects.Count != requestedProjectIds.Count)
                return Failed(new GoalEditResult.Invalid("ProjectIds", "Unknown project."));
        }

        // Only the sections that read or write memberships need the goal's current projects locked; a
        // details- or priority-only edit takes just the profile lock, like the order endpoint.
        var needsMemberships = req.Target is not null || requestedProjectIds is not null;
        var lockedProjectIds = (needsMemberships
                ? await db.ProjectGoals.Where(entry => entry.GoalId == goalId).Select(entry => entry.ProjectId).ToListAsync(ct)
                : [])
            .Union(requestedProjectIds ?? [])
            .ToList();

        // Same restart-on-membership-drift loop as the target and projects endpoints.
        while (true)
        {
            List<ProjectId>? restartWithProjectIds = null;
            var outcome = Failed(new GoalEditResult.NotFound());

            await planning.ExecuteLockedMutationAsync(lockedProjectIds, async transaction =>
            {
                // Re-read under the lock (Config/Events included) and re-derive the lock set from the
                // reloaded memberships; restart wider when they drifted.
                var reloaded = await db.ReloadGoalAsync(goal, ct);
                if (reloaded is null)
                    return;

                goal = reloaded;
                var memberships = await db.ProjectGoals.Where(entry => entry.GoalId == goalId).ToListAsync(ct);
                var membershipProjectIds = memberships.Select(entry => entry.ProjectId).ToList();
                var neededProjectIds = (needsMemberships ? membershipProjectIds : [])
                    .Union(requestedProjectIds ?? [])
                    .ToList();
                if (neededProjectIds.Except(lockedProjectIds).Any())
                {
                    restartWithProjectIds = neededProjectIds.Union(lockedProjectIds).ToList();
                    return;
                }

                var (result, order) = await ApplySectionsAsync(
                    req, profileId, goal, membershipProjectIds, requestedProjectIds, ownedProjects, memberships, ct);
                var finalProjectIds = requestedProjectIds?.ToList() ?? membershipProjectIds;
                if (result is GoalEditResult.Applied)
                {
                    result = await GoalEditSaver.SaveAsync(
                        db, planning, transaction, goalId,
                        new ProjectGoalSlotLookup(
                            finalProjectIds, goal.EntityType, goal.EntityId, goal.GoalType,
                            RankTargetKey.For(goal.GoalType, goal.Config), goal.Id),
                        membershipProjectIds, ct);
                    if (result is GoalEditResult.Applied && transaction is not null)
                        await transaction.CommitAsync(ct);
                }
                else if (transaction is not null)
                {
                    await transaction.RollbackAsync(ct);
                }

                outcome = new GoalEditOutcome(result, goal, finalProjectIds, order);
            }, ct);

            if (restartWithProjectIds is null)
                return outcome;
            lockedProjectIds = restartWithProjectIds;
        }
    }

    private static GoalEditOutcome Failed(GoalEditResult result) => new(result, null, [], null);

    private async Task<(GoalEditResult Result, GoalOrderSnapshot? Order)> ApplySectionsAsync(
        EditGoalRequest req,
        ProfileId profileId,
        Goal goal,
        List<ProjectId> membershipProjectIds,
        HashSet<ProjectId>? requestedProjectIds,
        List<Project> ownedProjects,
        List<ProjectGoal> memberships,
        CancellationToken ct)
    {
        GoalEditResult result;
        if (req.Target is { } target)
        {
            result = await targetEditor.ApplyAsync(
                profileId, goal, membershipProjectIds, target.Target, target.ExpectedRevision, ct);
            if (result is not GoalEditResult.Applied)
                return (result, null);
        }

        if (req.Details is { } details)
        {
            result = detailsEditor.Apply(goal, details);
            if (result is GoalEditResult.Invalid invalid)
                return (invalid with { Field = $"Details.{invalid.Field}" }, null);
        }

        if (requestedProjectIds is not null)
        {
            result = await membershipEditor.ApplyAsync(goal, requestedProjectIds, ownedProjects, memberships, ct);
            if (result is not GoalEditResult.Applied)
                return (result, null);
        }

        if (req.Priority is { } priority)
        {
            var moved = await orderService.MoveToPositionAsync(
                goal.Id, priority.Position, priority.ExpectedOrderRevision, ct);
            return (moved.Outcome switch
            {
                GoalOrderOutcome.Ok => new GoalEditResult.Applied(),
                GoalOrderOutcome.NotInFlight => new GoalEditResult.Invalid(
                    "Priority", "Only an active or paused goal has a position in the order."),
                GoalOrderOutcome.PositionOutOfRange => new GoalEditResult.Invalid(
                    "Priority.Position", "The position must be between 1 and the number of in-flight goals."),
                _ => new GoalEditResult.OrderConflict(moved),
            }, moved.Order);
        }

        return (new GoalEditResult.Applied(), null);
    }
}
