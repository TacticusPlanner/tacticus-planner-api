using TacticusPlanner.Domain.LegendaryEvents;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

/// <summary>The plan and team changes behind the plan endpoints: in-memory edits of the loaded plan aggregate
/// inside <see cref="LegendaryEventPlanWriter"/>, which saves it once. Inputs are already validated against the
/// catalog.</summary>
public static class LegendaryEventTeamMutations
{
    public static LegendaryEventPlanMutation UpdatePlan(LegendaryEventPlan plan, string? notes, bool showPaidOptions)
    {
        plan.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes;
        plan.ShowPaidOptions = showPaidOptions;
        return LegendaryEventPlanMutation.Applied;
    }

    /// <summary>Appends a team after the lane's existing teams. A bulk caller that already knows the lane's next
    /// position passes <paramref name="sortOrder"/> instead of a rescan per team. The caller enforces
    /// <see cref="LegendaryEventValidation.MaxTeamsPerLane"/> (<see cref="LaneIsFull"/>).</summary>
    public static LegendaryEventTeamRecord CreateTeam(
        LegendaryEventPlanMutationContext context, string laneId, LegendaryEventTeamFields fields, int? sortOrder = null)
    {
        var plan = context.Plan;
        var team = new LegendaryEventTeamRecord
        {
            Id = LegendaryEventTeamId.From(Guid.CreateVersion7()),
            LaneId = laneId,
            SortOrder = sortOrder ?? NextSortOrder(plan, laneId),
        };
        ApplyFields(context, team, fields);
        plan.Teams.Add(team);
        return team;
    }

    /// <summary>The lane's next sort order (orders are dense, so also its team count).</summary>
    public static int NextSortOrder(LegendaryEventPlan plan, string laneId) =>
        plan.Teams.Where(team => team.LaneId == laneId).Select(team => team.SortOrder + 1).DefaultIfEmpty(0).Max();

    public static bool LaneIsFull(LegendaryEventPlan plan, string laneId) =>
        plan.Teams.Count(team => team.LaneId == laneId) >= LegendaryEventValidation.MaxTeamsPerLane;

    /// <summary>Replaces the team's name, members, objectives and the given run's depth; lane, order and other
    /// runs' depths are kept.</summary>
    public static LegendaryEventPlanMutation UpdateTeam(
        LegendaryEventPlanMutationContext context, LegendaryEventTeamId teamId, LegendaryEventTeamFields fields)
    {
        var team = context.Plan.Teams.FirstOrDefault(entry => entry.Id == teamId);
        if (team is null)
            return LegendaryEventPlanMutation.TeamNotFound;

        ApplyFields(context, team, fields);
        return LegendaryEventPlanMutation.Applied;
    }

    /// <summary>Deletes the team and re-densifies the rest of its lane.</summary>
    public static LegendaryEventPlanMutation DeleteTeam(LegendaryEventPlanMutationContext context, LegendaryEventTeamId teamId)
    {
        var plan = context.Plan;
        var team = plan.Teams.FirstOrDefault(entry => entry.Id == teamId);
        if (team is null)
            return LegendaryEventPlanMutation.TeamNotFound;

        plan.Teams.Remove(team);
        Densify(plan.Teams.Where(entry => entry.LaneId == team.LaneId).OrderBy(entry => entry.SortOrder).ToList());
        return LegendaryEventPlanMutation.Applied;
    }

    /// <summary>Replaces the lane's order with <paramref name="teamIds"/>, which must be exactly the lane's team
    /// set. The current order is a no-op that keeps the revision.</summary>
    public static LegendaryEventPlanMutation ReorderLane(
        LegendaryEventPlan plan, string laneId, IReadOnlyList<Guid> teamIds)
    {
        var laneTeams = plan.Teams.Where(team => team.LaneId == laneId).OrderBy(team => team.SortOrder).ToList();
        if (teamIds.Distinct().Count() != teamIds.Count
            || !teamIds.ToHashSet().SetEquals(laneTeams.Select(team => team.Id.Value)))
        {
            return LegendaryEventPlanMutation.OrderSetMismatch;
        }

        if (laneTeams.Select(team => team.Id.Value).SequenceEqual(teamIds))
            return LegendaryEventPlanMutation.Unchanged;

        Densify(teamIds.Select(id => laneTeams.Single(team => team.Id.Value == id)).ToList());
        return LegendaryEventPlanMutation.Applied;
    }

    private static void Densify(List<LegendaryEventTeamRecord> ordered)
    {
        for (var order = 0; order < ordered.Count; order++)
            ordered[order].SortOrder = order;
    }

    private static void ApplyFields(
        LegendaryEventPlanMutationContext context, LegendaryEventTeamRecord team, LegendaryEventTeamFields fields)
    {
        team.Name = fields.Name!.Trim();
        team.MemberUnitIds = [.. fields.MemberUnitIds ?? []];
        team.ReserveUnitId = fields.ReserveUnitId;
        team.ObjectiveIndexes = (fields.ObjectiveIndexes ?? []).Distinct().Order().ToList();
        ApplyRunDepth(context, team, fields);
    }

    /// <summary>A depth upserts its run's entry (refreshing <c>recordedAt</c>), a null depth deletes it; other runs
    /// are untouched (design D12).</summary>
    private static void ApplyRunDepth(
        LegendaryEventPlanMutationContext context, LegendaryEventTeamRecord team, LegendaryEventTeamFields fields)
    {
        var existing = team.RunDepths.FirstOrDefault(depth => depth.Run == fields.Run);
        if (fields.ExpectedBattleClears is not { } clears)
        {
            if (existing is not null)
                team.RunDepths.Remove(existing);
            return;
        }

        if (fields.ExpectedBattleClearsSource is not { } source)
            throw new InvalidOperationException("The depth source must be validated before it is applied.");

        if (existing is null)
        {
            existing = new LegendaryEventTeamRunDepth { Run = fields.Run };
            // Appended, never re-sorted: owned JSON items are tracked by position. Reads order by run.
            team.RunDepths.Add(existing);
        }

        existing.ExpectedBattleClears = clears;
        existing.Source = source;
        existing.RecordedAt = context.Now;
    }
}
