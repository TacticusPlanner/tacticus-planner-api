using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

public sealed class ProjectGoalPlanningService(PlannerDbContext db)
{
    public static ProjectGoal CreateMembership(Project project, Goal goal, int priority, DateTimeOffset now) => new()
    {
        ProjectId = project.Id,
        GoalId = goal.Id,
        Priority = priority,
        EntityType = goal.EntityType,
        EntityId = goal.EntityId,
        GoalType = goal.GoalType,
        OccupiesInFlightSlot = IsInFlight(goal.Status),
        CreatedAt = now,
    };

    public async Task<ProjectGoalSlotConflictResponse?> FindConflictAsync(
        IEnumerable<ProjectId> projectIds,
        GoalEntityType entityType,
        string entityId,
        GoalType goalType,
        GoalId? excludingGoalId,
        CancellationToken ct)
    {
        var ids = projectIds.Distinct().ToList();
        var query = db.ProjectGoals
            .Where(entry => ids.Contains(entry.ProjectId)
                && entry.EntityType == entityType
                && entry.EntityId == entityId
                && entry.GoalType == goalType
                && entry.OccupiesInFlightSlot);

        if (excludingGoalId is { } excluded)
            query = query.Where(entry => entry.GoalId != excluded);

        return await query
            .Join(db.Projects, entry => entry.ProjectId, project => project.Id, (entry, project) => new { entry, project })
            .Select(value => new ProjectGoalSlotConflictResponse(
                "projectGoalSlotOccupied",
                $"{value.project.Name} already contains an active or paused {value.entry.GoalType} goal for this unit.",
                value.project.Id.Value,
                value.project.Name,
                value.entry.EntityType.ToString(),
                value.entry.EntityId,
                value.entry.GoalType.ToString(),
                value.entry.GoalId.Value))
            .FirstOrDefaultAsync(ct);
    }

    public async Task SyncOccupancyAsync(Goal goal, CancellationToken ct)
    {
        var memberships = await db.ProjectGoals.Where(entry => entry.GoalId == goal.Id).ToListAsync(ct);
        foreach (var membership in memberships)
            membership.OccupiesInFlightSlot = IsInFlight(goal.Status);
    }

    public async Task NormalizeAsync(IEnumerable<ProjectId> projectIds, CancellationToken ct)
    {
        foreach (var projectId in projectIds.Distinct())
        {
            var memberships = await LoadProjectMembershipsAsync(projectId, ct);
            var activeMemberships = memberships.Where(entry => entry.OccupiesInFlightSlot).ToList();
            var units = activeMemberships
                .GroupBy(UnitKey.From)
                .OrderBy(group => group.Min(entry => entry.Priority))
                .ThenBy(group => group.Key.EntityType)
                .ThenBy(group => group.Key.EntityId, StringComparer.Ordinal);

            var priority = 1;
            foreach (var unit in units)
                foreach (var membership in OrderGoals(unit.ToList()))
                    membership.Priority = priority++;

            foreach (var membership in memberships.Where(entry => !entry.OccupiesInFlightSlot))
                membership.Priority = priority++;
        }
    }

    public async Task<bool> ApplyUnitOrderAsync(ProjectId projectId, IReadOnlyList<UnitOrderEntryRequest> requested, CancellationToken ct)
    {
        var memberships = await LoadProjectMembershipsAsync(projectId, ct);
        var activeMemberships = memberships.Where(entry => entry.OccupiesInFlightSlot).ToList();
        var grouped = activeMemberships.GroupBy(UnitKey.From).ToDictionary(group => group.Key, group => group.ToList());
        var requestedKeys = requested.Select(entry => new UnitKey(
            Enum.Parse<GoalEntityType>(entry.EntityType, true), entry.EntityId.Trim())).ToList();

        if (requestedKeys.Count != grouped.Count
            || requestedKeys.Distinct().Count() != requestedKeys.Count
            || requestedKeys.Any(key => !grouped.ContainsKey(key)))
            return false;

        var priority = 1;
        foreach (var key in requestedKeys)
            foreach (var membership in OrderGoals(grouped[key]))
                membership.Priority = priority++;

        foreach (var membership in memberships.Where(entry => !entry.OccupiesInFlightSlot))
            membership.Priority = priority++;

        return true;
    }

    private async Task<List<ProjectGoal>> LoadProjectMembershipsAsync(ProjectId projectId, CancellationToken ct) =>
        await db.ProjectGoals
            .Include(entry => entry.Goal)
            .Where(entry => entry.ProjectId == projectId)
            .OrderBy(entry => entry.Priority)
            .ThenBy(entry => entry.GoalId)
            .ToListAsync(ct);

    private static List<ProjectGoal> OrderGoals(List<ProjectGoal> memberships)
    {
        var remaining = memberships.OrderBy(entry => entry.Priority).ThenBy(entry => entry.GoalId).ToList();
        var ids = remaining.Select(entry => entry.GoalId.Value).ToHashSet();
        var emitted = new HashSet<Guid>();
        var result = new List<ProjectGoal>(remaining.Count);

        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(entry =>
                entry.Goal is null || entry.Goal.DependsOn.Where(ids.Contains).All(emitted.Contains)) ?? remaining[0];
            remaining.Remove(next);
            result.Add(next);
            emitted.Add(next.GoalId.Value);
        }

        return result;
    }

    private static bool IsInFlight(GoalStatus status) => status is GoalStatus.Active or GoalStatus.Paused;

    private sealed record UnitKey(GoalEntityType EntityType, string EntityId)
    {
        public static UnitKey From(ProjectGoal entry) => new(entry.EntityType, entry.EntityId);
    }
}

public sealed record UnitOrderEntryRequest(string EntityType, string EntityId);

public sealed record ProjectGoalSlotConflictResponse(
    string IssueCode,
    string Message,
    Guid ProjectId,
    string ProjectName,
    string EntityType,
    string EntityId,
    string GoalType,
    Guid ExistingGoalId);
