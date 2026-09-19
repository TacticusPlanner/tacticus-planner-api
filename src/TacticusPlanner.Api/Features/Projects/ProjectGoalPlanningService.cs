using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

public sealed class ProjectGoalPlanningService(PlannerDbContext db)
{
    public async Task ExecuteLockedMutationAsync(
        IEnumerable<ProjectId> projectIds,
        Func<IDbContextTransaction?, Task> mutation,
        CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            await mutation(null);
            return;
        }

        var orderedProjectIds = projectIds.Distinct().OrderBy(id => id.Value).ToList();
        // strategy.ExecuteAsync is NOT a working retry here: it re-runs the delegate against the same
        // DbContext without resetting the change tracker (a retried attempt re-adds an already-tracked
        // entity and throws InvalidOperationException, which is not retried again), and several call
        // sites write the HTTP response from inside the delegate, so a retry after a partial write would
        // corrupt it. It stays only because EF Core requires a user-initiated transaction to be created
        // inside the strategy delegate. The design here avoids retryable failures by construction (see the
        // isolation-level comment below) rather than relying on this to absorb them. Upgrade path, if ever
        // needed: hoist response writes out of the delegate and call ChangeTracker.Clear() per attempt.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // READ COMMITTED, not SERIALIZABLE: the SELECT ... FOR UPDATE below is the actual mutual-
            // exclusion mechanism (locks are taken in ascending project id order, so multi-project
            // mutations cannot deadlock). READ COMMITTED is required so that a waiter, once unblocked,
            // takes a fresh per-statement snapshot and reads the state its predecessor just committed
            // instead of aborting on a transaction-wide snapshot taken before it started waiting. This is
            // safe only because every conflict- and ordering-relevant read (slot pre-check, next-priority
            // lookup, membership load) happens after the lock is held — standing constraint: any future
            // read used for a conflict or ordering decision must stay inside the lock, or this isolation
            // level becomes unsafe.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            foreach (var projectId in orderedProjectIds)
            {
                _ = await db.Database
                    .SqlQueryRaw<int>(
                        "SELECT 1 AS \"Value\" FROM projects WHERE id = {0} FOR UPDATE",
                        projectId.Value)
                    .SingleAsync(ct);
            }

            await mutation(transaction);
        });
    }

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

    public async Task<ProjectGoalSlotConflictResponse?> FindConflictAfterFailedSaveAsync(
        IDbContextTransaction? transaction,
        IEnumerable<ProjectGoalSlotLookup> slots,
        CancellationToken ct)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(ct);
            await transaction.DisposeAsync();
        }

        db.ChangeTracker.Clear();
        foreach (var slot in slots)
        {
            if (await FindConflictAsync(
                slot.ProjectIds,
                slot.EntityType,
                slot.EntityId,
                slot.GoalType,
                slot.ExcludingGoalId,
                ct) is { } conflict)
            {
                return conflict;
            }
        }

        return null;
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

public sealed record ProjectGoalSlotLookup(
    IReadOnlyCollection<ProjectId> ProjectIds,
    GoalEntityType EntityType,
    string EntityId,
    GoalType GoalType,
    GoalId? ExcludingGoalId = null);
