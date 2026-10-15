using TacticusPlanner.Domain.LegendaryEvents;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

/// <summary>Run input and annotation changes behind the Stage 3 plan endpoints, applied to a tracked plan inside
/// <see cref="LegendaryEventPlanWriter"/>. Inputs are already validated against the catalog. A write that changes
/// nothing answers <see cref="LegendaryEventPlanMutation.Unchanged"/> so the revision is kept (design D2, D6).</summary>
public static class LegendaryEventPlanInputMutations
{
    /// <summary>Upserts one run's inputs; other runs are untouched. Values equal to the stored row, or all zero
    /// for a run with no row, change nothing. There is no delete: zeros are the reset.</summary>
    public static LegendaryEventPlanMutation SetRunInputs(
        LegendaryEventPlanMutationContext context, int run, UpdateLegendaryEventRunInputsRequest request)
    {
        var plan = context.Plan;
        var existing = plan.Runs.FirstOrDefault(entry => entry.Run == run);
        if (existing is null)
        {
            if (request is { RegularMissions: 0, PremiumMissions: 0, BundlePurchased: false, CloseShards: 0 })
                return LegendaryEventPlanMutation.Unchanged;

            existing = new LegendaryEventRunProgress { PlanId = plan.Id, Run = run };
            plan.Runs.Add(existing);
            context.Db.Add(existing);
        }
        else if (existing.RegularMissions == request.RegularMissions
            && existing.PremiumMissions == request.PremiumMissions
            && existing.BundlePurchased == request.BundlePurchased
            && existing.CloseShards == request.CloseShards)
        {
            return LegendaryEventPlanMutation.Unchanged;
        }

        existing.RegularMissions = request.RegularMissions;
        existing.PremiumMissions = request.PremiumMissions;
        existing.BundlePurchased = request.BundlePurchased;
        existing.CloseShards = request.CloseShards;
        existing.UpdatedAt = context.Now;
        return LegendaryEventPlanMutation.Applied;
    }

    /// <summary>Sets <paramref name="status"/> on the listed cells of one battle, or removes their marks when it
    /// is null; every other cell is untouched.</summary>
    public static LegendaryEventPlanMutation SetAnnotations(
        LegendaryEventPlanMutationContext context,
        string laneId,
        int battleIndex,
        IReadOnlyList<int> objectiveIds,
        LegendaryEventAnnotationStatus? status)
    {
        var plan = context.Plan;
        var changed = false;
        foreach (var objectiveId in objectiveIds)
        {
            var existing = plan.Annotations.FirstOrDefault(entry =>
                entry.LaneId == laneId && entry.BattleIndex == battleIndex && entry.ObjectiveId == objectiveId);
            if (status is not { } value)
            {
                if (existing is null)
                    continue;

                plan.Annotations.Remove(existing);
                context.Db.Remove(existing);
                changed = true;
                continue;
            }

            if (existing is null)
            {
                existing = new LegendaryEventObjectiveAnnotation
                {
                    PlanId = plan.Id,
                    LaneId = laneId,
                    BattleIndex = battleIndex,
                    ObjectiveId = objectiveId,
                };
                plan.Annotations.Add(existing);
                context.Db.Add(existing);
            }
            else if (existing.Status == value)
            {
                continue;
            }

            existing.Status = value;
            existing.UpdatedAt = context.Now;
            changed = true;
        }

        return changed ? LegendaryEventPlanMutation.Applied : LegendaryEventPlanMutation.Unchanged;
    }
}
