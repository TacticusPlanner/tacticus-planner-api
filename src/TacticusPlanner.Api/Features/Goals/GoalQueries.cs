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
}
