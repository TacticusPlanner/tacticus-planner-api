using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.GameDomain;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>The progression floor that a unit's stored Ascension prerequisites raise a target's rarity cap to,
/// shared by combined creation and target edits so both accept the same targets.</summary>
public static class AscensionFloor
{
    /// <summary>The unit's stored goals other than Archived, keyed by id, for <see cref="Reachable"/>.</summary>
    public static async Task<Dictionary<Guid, Goal>> LoadUnitGoalsAsync(
        PlannerDbContext db, GoalEntityType entityType, string entityId, CancellationToken ct) =>
        (await db.Goals.AsNoTracking()
            .Where(goal => goal.EntityType == entityType && goal.EntityId == entityId
                && goal.Status != GoalStatus.Archived)
            .ToListAsync(ct))
        .ToDictionary(goal => goal.Id.Value);

    /// <summary>The highest end of the Ascension goals reachable from <paramref name="roots"/> through stored
    /// <see cref="Goal.DependsOn"/> edges, at any depth. Completed prerequisites are traversed too, so an
    /// Ascension behind a finished goal still counts. Each goal is visited once, so a cycle in stored data
    /// terminates.</summary>
    public static UnitProgression? Reachable(IEnumerable<Guid> roots, IReadOnlyDictionary<Guid, Goal> unitGoals)
    {
        UnitProgression? floor = null;
        var visited = new HashSet<Guid>();
        var pending = new Stack<Guid>(roots);
        while (pending.TryPop(out var id))
        {
            if (!visited.Add(id) || !unitGoals.TryGetValue(id, out var goal))
                continue;
            if (goal.GoalType == GoalType.Ascension && goal.Config.Progression is { } progression)
                floor = Max(floor, ProgressionRules.FromWireName(progression.End));
            foreach (var dependency in goal.DependsOn)
                pending.Push(dependency);
        }

        return floor;
    }

    public static UnitProgression? Max(UnitProgression? a, UnitProgression? b) =>
        a is null ? b : b is null ? a : (UnitProgression)Math.Max((int)a, (int)b);
}
