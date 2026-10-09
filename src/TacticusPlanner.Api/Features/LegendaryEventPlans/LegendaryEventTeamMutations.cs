using TacticusPlanner.Domain.LegendaryEvents;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

/// <summary>The plan and team changes behind the plan endpoints, applied to a tracked plan inside
/// <see cref="LegendaryEventPlanWriter"/>. Inputs are already validated against the catalog.</summary>
public static class LegendaryEventTeamMutations
{
    public static LegendaryEventPlanMutation UpdatePlan(LegendaryEventPlan plan, string? notes, bool showPaidOptions)
    {
        plan.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes;
        plan.ShowPaidOptions = showPaidOptions;
        return LegendaryEventPlanMutation.Applied;
    }

    /// <summary>Appends a team after the lane's existing teams.</summary>
    public static LegendaryEventTeam CreateTeam(
        LegendaryEventPlanMutationContext context, string laneId, LegendaryEventTeamFields fields)
    {
        var plan = context.Plan;
        var laneTeams = plan.Teams.Where(team => team.LaneId == laneId).ToList();
        var team = new LegendaryEventTeam
        {
            Id = LegendaryEventTeamId.From(Guid.NewGuid()),
            PlanId = plan.Id,
            LaneId = laneId,
            Name = fields.Name!.Trim(),
            SortOrder = laneTeams.Count == 0 ? 0 : laneTeams.Max(entry => entry.SortOrder) + 1,
        };
        foreach (var member in DesiredMembers(team.Id, fields))
            team.Members.Add(member);
        foreach (var index in (fields.ObjectiveIndexes ?? []).Distinct())
            team.Objectives.Add(new LegendaryEventTeamObjective { TeamId = team.Id, ObjectiveIndex = index });
        plan.Teams.Add(team);
        context.Db.Add(team);
        ApplyRunDepth(context, team, fields);
        return team;
    }

    /// <summary>Replaces the team's name, members, objectives and the given run's depth; lane, order and other
    /// runs' depths are kept.</summary>
    public static async Task<LegendaryEventPlanMutation> UpdateTeamAsync(
        LegendaryEventPlanMutationContext context,
        LegendaryEventTeamId teamId,
        LegendaryEventTeamFields fields,
        CancellationToken ct)
    {
        var team = context.Plan.Teams.FirstOrDefault(entry => entry.Id == teamId);
        if (team is null)
            return LegendaryEventPlanMutation.TeamNotFound;

        team.Name = fields.Name!.Trim();

        var desiredMembers = DesiredMembers(team.Id, fields);
        var staleMembers = team.Members
            .Where(member => !desiredMembers.Any(desired => SameMember(desired, member)))
            .ToList();
        var desiredObjectives = (fields.ObjectiveIndexes ?? []).ToHashSet();
        var staleObjectives = team.Objectives
            .Where(objective => !desiredObjectives.Contains(objective.ObjectiveIndex))
            .ToList();
        if (staleMembers.Count > 0 || staleObjectives.Count > 0)
        {
            foreach (var member in staleMembers)
                team.Members.Remove(member);
            foreach (var objective in staleObjectives)
                team.Objectives.Remove(objective);
            context.Db.RemoveRange(staleMembers);
            context.Db.RemoveRange(staleObjectives);
            await context.FlushRemovalsAsync(ct);
        }

        foreach (var member in desiredMembers.Where(desired => !team.Members.Any(member => SameMember(desired, member))))
        {
            team.Members.Add(member);
            context.Db.Add(member);
        }

        foreach (var index in desiredObjectives.Where(index => team.Objectives.All(entry => entry.ObjectiveIndex != index)))
        {
            var objective = new LegendaryEventTeamObjective { TeamId = team.Id, ObjectiveIndex = index };
            team.Objectives.Add(objective);
            context.Db.Add(objective);
        }

        ApplyRunDepth(context, team, fields);
        return LegendaryEventPlanMutation.Applied;
    }

    /// <summary>Deletes the team (its rows cascade) and re-densifies the rest of its lane.</summary>
    public static LegendaryEventPlanMutation DeleteTeam(LegendaryEventPlanMutationContext context, LegendaryEventTeamId teamId)
    {
        var plan = context.Plan;
        var team = plan.Teams.FirstOrDefault(entry => entry.Id == teamId);
        if (team is null)
            return LegendaryEventPlanMutation.TeamNotFound;

        context.Db.Remove(team);
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

    private static void Densify(List<LegendaryEventTeam> ordered)
    {
        for (var order = 0; order < ordered.Count; order++)
        {
            if (ordered[order].SortOrder != order)
                ordered[order].SortOrder = order;
        }
    }

    private static List<LegendaryEventTeamMember> DesiredMembers(LegendaryEventTeamId teamId, LegendaryEventTeamFields fields)
    {
        var members = (fields.MemberUnitIds ?? [])
            .Select((unitId, position) => new LegendaryEventTeamMember
            {
                TeamId = teamId,
                Reserve = false,
                Position = position,
                UnitId = unitId,
            })
            .ToList();
        if (fields.ReserveUnitId is { } reserve)
            members.Add(new LegendaryEventTeamMember { TeamId = teamId, Reserve = true, Position = 0, UnitId = reserve });
        return members;
    }

    private static bool SameMember(LegendaryEventTeamMember left, LegendaryEventTeamMember right) =>
        left.Reserve == right.Reserve && left.Position == right.Position && left.UnitId == right.UnitId;

    /// <summary>A depth upserts its run's row (refreshing <c>recordedAt</c>), a null depth deletes it; other runs
    /// are untouched (design D12).</summary>
    private static void ApplyRunDepth(
        LegendaryEventPlanMutationContext context, LegendaryEventTeam team, LegendaryEventTeamFields fields)
    {
        var existing = team.RunDepths.FirstOrDefault(depth => depth.Run == fields.Run);
        if (fields.ExpectedBattleClears is not { } clears)
        {
            if (existing is not null)
            {
                team.RunDepths.Remove(existing);
                context.Db.Remove(existing);
            }

            return;
        }

        if (!LegendaryEventCatalogValidator.TryParseSource(fields.ExpectedBattleClearsSource, out var source))
            throw new InvalidOperationException("The depth source must be validated before it is applied.");

        if (existing is null)
        {
            existing = new LegendaryEventTeamRunDepth { TeamId = team.Id, Run = fields.Run };
            team.RunDepths.Add(existing);
            context.Db.Add(existing);
        }

        existing.ExpectedBattleClears = clears;
        existing.Source = source;
        existing.RecordedAt = context.Now;
    }
}
