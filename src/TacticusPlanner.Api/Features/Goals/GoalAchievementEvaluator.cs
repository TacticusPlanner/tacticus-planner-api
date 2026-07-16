using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>Completes goals whose concrete target state is already present in the latest player snapshot.</summary>
public sealed class GoalAchievementEvaluator(PlannerDbContext db, TimeProvider timeProvider)
{
    public async Task<int> EvaluateAsync(ProfileId profileId, PlayerDataSnapshot snapshot, CancellationToken ct)
    {
        var goals = await db.Goals
            .Where(goal => goal.ProfileId == profileId
                && goal.Status != GoalStatus.Completed
                && goal.Status != GoalStatus.Archived
                && goal.Status != GoalStatus.Deleted)
            .ToListAsync(ct);
        var now = timeProvider.GetUtcNow();
        var completed = 0;

        foreach (var goal in goals)
        {
            foreach (var milestone in goal.Milestones.Where(milestone =>
                milestone.Status != "completed" && IsMilestoneAchieved(goal, milestone, snapshot)))
            {
                milestone.Status = "completed";
                milestone.CompletedAt ??= now;
            }

            if (!IsAchieved(goal, snapshot))
            {
                continue;
            }

            goal.Status = GoalStatus.Completed;
            foreach (var milestone in goal.Milestones.Where(milestone => milestone.Status != "completed"))
            {
                milestone.Status = "completed";
                milestone.CompletedAt ??= now;
            }

            if (!goal.Events.Any(goalEvent => goalEvent.Type == GoalEventType.Completed))
            {
                goal.Events.Add(new GoalEvent { At = now, Type = GoalEventType.Completed });
            }

            completed++;
        }

        return completed;
    }

    public static bool IsMilestoneAchieved(
        Goal goal,
        GoalMilestone milestone,
        PlayerDataSnapshot snapshot)
    {
        var character = snapshot.Characters.FirstOrDefault(unit => unit.UnitId.Value == goal.EntityId);
        var mow = snapshot.Mows.FirstOrDefault(unit => unit.UnitId.Value == goal.EntityId);
        var unit = (PlayerBaseUnitRecord?)character ?? mow;

        return milestone.Kind switch
        {
            "rank" when character is not null
                && Enum.TryParse<UnitRank>(milestone.TargetState, out var targetRank) =>
                (int)character.Rank > (int)targetRank
                || (character.Rank == targetRank
                    && (!IsFinalMilestone(goal, milestone) || RankAchieved(character, goal.Config.Rank))),
            "progression" when unit is not null =>
                ProgressionAchieved(unit.ProgressionIndex, new ProgressionTarget
                {
                    Start = milestone.TargetState,
                    End = milestone.TargetState,
                }),
            "ability" when unit is not null
                && int.TryParse(milestone.TargetState, out var level) =>
                SelectedAbilityLevel(unit, goal.Config.Ability) >= level,
            _ => false,
        };
    }

    public static bool IsAchieved(Goal goal, PlayerDataSnapshot snapshot)
    {
        var character = snapshot.Characters.FirstOrDefault(unit => unit.UnitId.Value == goal.EntityId);
        var mow = snapshot.Mows.FirstOrDefault(unit => unit.UnitId.Value == goal.EntityId);
        var unit = (PlayerBaseUnitRecord?)character ?? mow;

        return goal.GoalType switch
        {
            GoalType.Unlock => unit is not null,
            GoalType.Rank => character is not null && RankAchieved(character, goal.Config.Rank),
            GoalType.Ascension => unit is not null && ProgressionAchieved(unit.ProgressionIndex, goal.Config.Progression),
            GoalType.Ability => unit is not null && AbilityAchieved(unit, goal.Config.Ability),
            _ => false,
        };
    }

    private static bool RankAchieved(PlayerCharacterRecord character, RankTarget? target)
    {
        if (target is null) return false;
        if ((int)character.Rank > target.End) return true;
        if ((int)character.Rank < target.End) return false;
        var requiredApplied = Math.Max(target.EndAppliedUpgrades, target.EndPointFive ? 3 : 0);
        return character.AppliedUpgradeSlots.Distinct().Count() >= requiredApplied;
    }

    private static bool ProgressionAchieved(UnitProgression current, ProgressionTarget? target)
    {
        if (target is null) return false;
        var targetIndex = GameCatalogGoalLookups.ProgressionIndex(target.End);
        return targetIndex >= 0 && (int)current >= targetIndex;
    }

    private static bool AbilityAchieved(PlayerBaseUnitRecord unit, AbilityTarget? target)
    {
        if (target is null) return false;
        var first = unit.Abilities.ElementAtOrDefault(0)?.Level ?? 1;
        var second = unit.Abilities.ElementAtOrDefault(1)?.Level ?? 1;
        return first >= target.ActiveEnd && second >= target.PassiveEnd;
    }

    private static int SelectedAbilityLevel(PlayerBaseUnitRecord unit, AbilityTarget? target)
    {
        if (target is null) return 0;
        var first = unit.Abilities.ElementAtOrDefault(0)?.Level ?? 1;
        var second = unit.Abilities.ElementAtOrDefault(1)?.Level ?? 1;
        return target.ActiveEnd > target.ActiveStart ? first : second;
    }

    private static bool IsFinalMilestone(Goal goal, GoalMilestone milestone) =>
        milestone.Index == goal.Milestones.Max(item => item.Index);
}
