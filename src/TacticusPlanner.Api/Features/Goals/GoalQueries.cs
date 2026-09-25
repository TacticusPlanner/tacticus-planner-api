using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

public static class GoalQueries
{
    /// <summary>The ids of every project this goal currently belongs to — used to populate
    /// <see cref="GoalDetailResponse.ProjectIds"/> via <see cref="GoalMapper.ToDetail"/> without loading
    /// full <c>ProjectGoal</c> rows.</summary>
    public static Task<List<Guid>> ProjectIdsAsync(this PlannerDbContext db, GoalId goalId, CancellationToken ct) =>
        db.ProjectGoals
            .Where(entity => entity.GoalId == goalId)
            .Select(entity => entity.ProjectId.Value)
            .ToListAsync(ct);

    /// <summary>
    /// Re-reads a tracked goal from the database, including its JSON-owned <c>Config</c> and <c>Events</c>.
    /// <c>Entry(goal).ReloadAsync()</c> is not enough: it refreshes the goal's own columns (status,
    /// revision) but leaves the owned JSON payloads at what was loaded before a lock wait — which now
    /// matters because <c>Config</c> can change after creation (in-place target edit). Call this after
    /// acquiring the project locks, before any decision that reads the goal's target. Returns null when the
    /// goal was deleted in the meantime.
    /// </summary>
    public static async Task<Goal?> ReloadGoalAsync(this PlannerDbContext db, Goal goal, CancellationToken ct)
    {
        var id = goal.Id;
        db.Entry(goal).State = EntityState.Detached;
        return await db.Goals.FirstOrDefaultAsync(entity => entity.Id == id, ct);
    }
}
