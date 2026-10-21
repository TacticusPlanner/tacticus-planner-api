using FastEndpoints;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>The goal conflicts in the unified error shape (<c>api-error-contract</c>), each carrying the
/// conflicting resource in its read shape under <c>current</c>.</summary>
public static class GoalProblems
{
    public const string SlotOccupied = "slotOccupied";
    public const string GoalRevisionStale = "goalRevisionStale";
    public const string GoalOrderStale = "goalOrderStale";

    /// <summary>409 <c>slotOccupied</c>: names the goal that wanted the slot (<c>goalId</c>), the slot (entity, goal
    /// type, Rank's normalized end target) and the goal holding it (<c>existingGoalId</c>, detail under
    /// <c>current</c>).</summary>
    public static async Task SendSlotConflictAsync(
        IEndpoint endpoint, GoalSlotConflict conflict, CancellationToken ct)
    {
        var (candidate, existing) = conflict;
        var db = endpoint.HttpContext.Resolve<PlannerDbContext>();
        var target = RankTargetKey.For(existing.GoalType, existing.Config);
        await endpoint.SendConflictAsync(
            SlotOccupied,
            target is null
                ? $"An active or paused {existing.GoalType} goal for this unit already exists."
                : $"An active or paused {existing.GoalType} goal with this target for this unit already exists.",
            new GoalMapper().ToDetail(existing, await db.ProjectIdsAsync(existing.Id, ct)),
            ct,
            new Dictionary<string, object?>
            {
                ["goalId"] = candidate.Id.Value,
                ["entityType"] = existing.EntityType.ToString(),
                ["entityId"] = existing.EntityId,
                ["goalType"] = existing.GoalType.ToString(),
                ["normalizedTarget"] = target,
                ["existingGoalId"] = existing.Id.Value,
            });
    }

    /// <summary>409 <c>goalRevisionStale</c> with the goal as it stands.</summary>
    public static Task SendStaleRevisionAsync(IEndpoint endpoint, GoalDetailResponse current, CancellationToken ct) =>
        endpoint.SendConflictAsync(
            GoalRevisionStale, "The goal changed since it was loaded. Review the current goal and try again.", current, ct);
}
