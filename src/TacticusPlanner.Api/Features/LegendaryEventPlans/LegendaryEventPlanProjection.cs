using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

/// <summary>Loads a plan with everything beneath it and maps it to the served shape. The one projection
/// every plan endpoint, the writer's 409 body and the V1 import share, so ordering rules live here only.</summary>
public sealed class LegendaryEventPlanProjection(PlannerDbContext db)
{
    /// <summary>The plan for <paramref name="eventId"/>, tracked, with teams, members, objectives and run
    /// depths loaded; null when the profile has none. Scoped to the caller by the global query filter.</summary>
    public Task<LegendaryEventPlan?> LoadTrackedAsync(string eventId, CancellationToken ct) =>
        Query().FirstOrDefaultAsync(plan => plan.EventId == eventId, ct);

    public async Task<LegendaryEventPlanResponse> ReadAsync(string eventId, CancellationToken ct)
    {
        var plan = await Query().AsNoTracking().FirstOrDefaultAsync(entity => entity.EventId == eventId, ct);
        return plan is null ? Empty(eventId) : ToResponse(plan);
    }

    /// <summary>What a missing plan reads as: revision 0, no teams, the current catalog version.</summary>
    public static LegendaryEventPlanResponse Empty(string eventId) =>
        new(eventId, 0, GameCatalogRelease.Version, null, false, []);

    public static LegendaryEventPlanResponse ToResponse(LegendaryEventPlan plan) => new(
        plan.EventId,
        plan.Revision,
        plan.CatalogVersion,
        plan.Notes,
        plan.ShowPaidOptions,
        plan.Teams
            .OrderBy(team => LegendaryEventPlanRules.LaneOrder(team.LaneId))
            .ThenBy(team => team.SortOrder)
            .Select(ToResponse)
            .ToArray());

    public static LegendaryEventTeamResponse ToResponse(LegendaryEventTeam team) => new(
        team.Id.Value,
        team.LaneId,
        team.Name,
        team.SortOrder,
        team.Members.Where(member => !member.Reserve).OrderBy(member => member.Position).Select(member => member.UnitId).ToArray(),
        team.Members.FirstOrDefault(member => member.Reserve)?.UnitId,
        team.Objectives.Select(objective => objective.ObjectiveIndex).Order().ToArray(),
        team.RunDepths
            .OrderBy(depth => depth.Run)
            .Select(depth => new LegendaryEventTeamRunDepthResponse(
                depth.Run, depth.ExpectedBattleClears, LegendaryEventDepthSources.ToWire(depth.Source), depth.RecordedAt))
            .ToArray());

    private IQueryable<LegendaryEventPlan> Query() =>
        db.LegendaryEventPlans
            .Include(plan => plan.Teams).ThenInclude(team => team.Members)
            .Include(plan => plan.Teams).ThenInclude(team => team.Objectives)
            .Include(plan => plan.Teams).ThenInclude(team => team.RunDepths)
            .AsSplitQuery();
}
