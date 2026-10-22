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
    GoalEditResult Result, Goal? Goal, List<Guid> ProjectIds, GoalOrderSnapshot? Order);

/// <summary>
/// The all-or-nothing edit behind <see cref="EditGoalEndpoint"/> (<c>goal-combined-edit</c>): the target,
/// details, projects and priority sections run in that order inside one profile-locked transaction, against the
/// goal and memberships re-read under the lock, with one <c>SaveChangesAsync</c> and one commit. Any non-applied
/// result rolls the transaction back. It writes no HTTP response.
/// </summary>
public sealed class GoalCombinedEditor(
    PlannerDbContext db,
    ProjectGoalPlanningService planning,
    GoalTargetEditor targetEditor,
    GoalDetailsEditor detailsEditor,
    GoalMembershipEditor membershipEditor,
    GoalOrderService orderService,
    TimeProvider timeProvider)
{
    public async Task<GoalEditOutcome> ApplyAsync(
        ProfileId profileId, GoalId goalId, EditGoalRequest req, CancellationToken ct)
    {
        var requestedProjectIds = req.ProjectIds?.Select(ProjectId.From).ToHashSet();
        var requested = requestedProjectIds?.ToList() ?? [];
        var outcome = Failed(new GoalEditResult.NotFound());
        await planning.ExecuteLockedMutationAsync(async transaction =>
        {
            // Read under the lock, so a status/target/membership change committed while this request waited is
            // seen: the goal (tracked), its memberships and how many requested projects the caller owns, in one
            // statement. Scoped to the caller's profile by PlannerDbContext's global query filter.
            var row = await db.Goals
                .Where(entity => entity.Id == goalId)
                .Select(entity => new
                {
                    Goal = entity,
                    Memberships = db.ProjectGoals.Where(entry => entry.GoalId == entity.Id).ToList(),
                    OwnedRequested = db.Projects.Count(project => requested.Contains(project.Id)),
                })
                .FirstOrDefaultAsync(ct);
            if (row is null)
                return;

            if (requestedProjectIds is not null && row.OwnedRequested != requestedProjectIds.Count)
            {
                outcome = Failed(new GoalEditResult.Invalid("ProjectIds", "Unknown project."));
                return;
            }

            var goal = row.Goal;
            var memberships = row.Memberships;
            var projectIds = memberships.Select(entry => entry.ProjectId.Value).ToList();

            var (result, order) = await ApplySectionsAsync(req, profileId, goal, projectIds, requestedProjectIds, memberships, ct);
            if (requestedProjectIds is not null)
                projectIds = requestedProjectIds.Select(id => id.Value).ToList();

            if (result is GoalEditResult.Applied)
            {
                result = await GoalEditSaver.SaveAsync(db, transaction, goal, ct);
                if (result is GoalEditResult.Applied)
                    await transaction.CommitAsync(ct);
            }
            else
            {
                await transaction.RollbackAsync(ct);
            }

            outcome = new GoalEditOutcome(result, goal, projectIds, order);
        }, ct);
        return outcome;
    }

    private static GoalEditOutcome Failed(GoalEditResult result) => new(result, null, [], null);

    private async Task<(GoalEditResult Result, GoalOrderSnapshot? Order)> ApplySectionsAsync(
        EditGoalRequest req,
        ProfileId profileId,
        Goal goal,
        List<Guid> projectIds,
        HashSet<ProjectId>? requestedProjectIds,
        List<ProjectGoal> memberships,
        CancellationToken ct)
    {
        if (req.Target is { } target)
        {
            var result = await targetEditor.ApplyAsync(profileId, goal, projectIds, target.Target, target.ExpectedRevision, ct);
            if (result is not GoalEditResult.Applied)
                return (result, null);
        }

        if (req.Details is { } details && detailsEditor.Apply(goal, details) is GoalEditResult.Invalid invalidDetails)
            return (invalidDetails with { Field = $"Details.{invalidDetails.Field}" }, null);

        if (requestedProjectIds is not null)
            membershipEditor.Apply(goal, requestedProjectIds, memberships, timeProvider.GetUtcNow());

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
