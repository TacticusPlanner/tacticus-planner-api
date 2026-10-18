using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

/// <summary>Loads a plan with everything beneath it and maps it to the single served shape (design D8).</summary>
public static class LegendaryEventPlanProjection
{
    public static IQueryable<LegendaryEventPlan> WithTeams(this IQueryable<LegendaryEventPlan> plans) =>
        plans
            .Include(plan => plan.Teams).ThenInclude(team => team.Members)
            .Include(plan => plan.Teams).ThenInclude(team => team.Objectives)
            .Include(plan => plan.Teams).ThenInclude(team => team.RunDepths)
            .Include(plan => plan.Runs)
            .Include(plan => plan.Annotations)
            .AsSplitQuery();

    /// <summary>Scoped to the caller's profile by <see cref="PlannerDbContext"/>'s global query filter.</summary>
    public static Task<LegendaryEventPlan?> LoadPlanAsync(
        this PlannerDbContext db, string eventId, bool tracked, CancellationToken ct)
    {
        var plans = tracked ? db.LegendaryEventPlans : db.LegendaryEventPlans.AsNoTracking();
        return plans.WithTeams().FirstOrDefaultAsync(plan => plan.EventId == eventId, ct);
    }

    public static LegendaryEventPlanResponse Empty(string eventId) =>
        new(eventId, 0, GameCatalogRelease.Version, null, false, [], [], []);

    public static LegendaryEventPlanResponse Map(LegendaryEventPlan plan) =>
        new(
            plan.EventId,
            plan.Revision,
            plan.CatalogVersion,
            plan.Notes,
            plan.ShowPaidOptions,
            plan.Teams
                .OrderBy(team => LaneOrder(team.LaneId))
                .ThenBy(team => team.SortOrder)
                .Select(MapTeam)
                .ToList(),
            plan.Runs
                .OrderBy(run => run.Run)
                .Select(run => new LegendaryEventRunProgressResponse(
                    run.Run, run.RegularMissions, run.PremiumMissions,
                    run.CompletedRegularMissions.Order().ToList(), run.CompletedPremiumMissions.Order().ToList(),
                    run.BundlePurchased, run.CloseShards, run.UpdatedAt))
                .ToList(),
            plan.Annotations
                .OrderBy(annotation => LaneOrder(annotation.LaneId))
                .ThenBy(annotation => annotation.BattleIndex)
                .ThenBy(annotation => annotation.ObjectiveId)
                .Select(annotation => new LegendaryEventObjectiveAnnotationResponse(
                    annotation.LaneId, annotation.BattleIndex, annotation.ObjectiveId,
                    StatusName(annotation.Status), annotation.UpdatedAt))
                .ToList());

    public static string StatusName(LegendaryEventAnnotationStatus status) => status switch
    {
        LegendaryEventAnnotationStatus.StopHere => LegendaryEventValidation.StopHereStatus,
        _ => LegendaryEventValidation.MaybeClearStatus,
    };

    public static string SourceName(LegendaryEventDepthSource source) => source switch
    {
        LegendaryEventDepthSource.Estimate => "estimate",
        _ => "manual",
    };

    private static LegendaryEventTeamResponse MapTeam(LegendaryEventTeam team) =>
        new(
            team.Id.Value,
            team.LaneId,
            team.Name,
            team.SortOrder,
            team.Members.Where(member => !member.Reserve).OrderBy(member => member.Position)
                .Select(member => member.UnitId).ToList(),
            team.Members.FirstOrDefault(member => member.Reserve)?.UnitId,
            team.Objectives.Select(objective => objective.ObjectiveIndex).Order().ToList(),
            team.RunDepths.OrderBy(depth => depth.Run)
                .Select(depth => new LegendaryEventTeamRunDepthResponse(
                    depth.Run, depth.ExpectedBattleClears, SourceName(depth.Source), depth.RecordedAt))
                .ToList());

    public static int LaneOrder(string laneId)
    {
        for (var index = 0; index < LegendaryEventValidation.LaneIds.Count; index++)
        {
            if (LegendaryEventValidation.LaneIds[index] == laneId)
                return index;
        }

        return LegendaryEventValidation.LaneIds.Count;
    }
}
