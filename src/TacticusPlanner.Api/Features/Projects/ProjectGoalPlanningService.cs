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
            // The profile row is locked first, always (FOR NO KEY UPDATE: its key is never written, so inserts
            // of rows that reference it, which take FOR KEY SHARE, are not blocked): it serializes every mutation of the account's global
            // goal order (see GoalOrderService) and fixes the lock order (profile, then projects ascending),
            // so paths that need both cannot deadlock.
            var profileId = await db.Profiles.Select(profile => profile.Id).SingleAsync(ct);
            _ = await db.Database
                .SqlQueryRaw<int>("SELECT 1 AS \"Value\" FROM profiles WHERE id = {0} FOR NO KEY UPDATE", profileId.Value)
                .SingleAsync(ct);
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

    public static ProjectGoal CreateMembership(Project project, Goal goal, DateTimeOffset now) => new()
    {
        ProjectId = project.Id,
        GoalId = goal.Id,
        EntityType = goal.EntityType,
        EntityId = goal.EntityId,
        GoalType = goal.GoalType,
        OccupiesInFlightSlot = IsInFlight(goal.Status),
        RankTargetKey = RankTargetKey.For(goal.GoalType, goal.Config),
        CreatedAt = now,
    };

    public async Task<ProjectGoalSlotConflictResponse?> FindConflictAsync(
        IEnumerable<ProjectId> projectIds,
        GoalEntityType entityType,
        string entityId,
        GoalType goalType,
        string? rankTargetKey,
        GoalId? excludingGoalId,
        CancellationToken ct)
    {
        var ids = projectIds.Distinct().ToList();
        // rankTargetKey is null for every non-Rank goal, so this compares null to null there (same slot as
        // before) and only separates Rank goals by their normalized end target.
        var query = db.ProjectGoals
            .Where(entry => ids.Contains(entry.ProjectId)
                && entry.EntityType == entityType
                && entry.EntityId == entityId
                && entry.GoalType == goalType
                && entry.RankTargetKey == rankTargetKey
                && entry.OccupiesInFlightSlot);

        if (excludingGoalId is { } excluded)
            query = query.Where(entry => entry.GoalId != excluded);

        // Every occupied project is reported (the first is the top-level conflict for existing callers, the
        // full list is in Conflicts) so a multi-project change can show all of them, not just one.
        var found = await query
            .Join(db.Projects, entry => entry.ProjectId, project => project.Id, (entry, project) => new { entry, project })
            .OrderBy(value => value.project.Id)
            .ToListAsync(ct);
        if (found.Count == 0)
            return null;

        var first = found[0];
        return new ProjectGoalSlotConflictResponse(
            "projectGoalSlotOccupied",
            ConflictMessage(first.project.Name, first.entry.GoalType, first.entry.RankTargetKey),
            first.project.Id.Value,
            first.project.Name,
            first.entry.EntityType.ToString(),
            first.entry.EntityId,
            first.entry.GoalType.ToString(),
            first.entry.GoalId.Value,
            first.entry.RankTargetKey,
            found
                .Select(value => new ProjectGoalSlotConflictEntry(
                    value.project.Id.Value, value.project.Name, value.entry.GoalId.Value))
                .ToList());
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
                slot.RankTargetKey,
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

    /// <summary>The 409 message shared by every slot-conflict site (this service and the project-side
    /// membership endpoint's in-request duplicate check).</summary>
    public static string ConflictMessage(string projectName, GoalType goalType, string? rankTargetKey) =>
        rankTargetKey is null
            ? $"{projectName} already contains an active or paused {goalType} goal for this unit."
            : $"{projectName} already contains an active or paused {goalType} goal with this target for this unit.";

    private static bool IsInFlight(GoalStatus status) => status is GoalStatus.Active or GoalStatus.Paused;
}

public sealed record ProjectGoalSlotConflictResponse(
    string IssueCode,
    string Message,
    Guid ProjectId,
    string ProjectName,
    string EntityType,
    string EntityId,
    string GoalType,
    Guid ExistingGoalId,
    string? NormalizedTarget = null,
    IReadOnlyList<ProjectGoalSlotConflictEntry>? Conflicts = null);

/// <summary>One occupied project and the goal holding the slot there — one per conflicting project.</summary>
public sealed record ProjectGoalSlotConflictEntry(Guid ProjectId, string ProjectName, Guid ExistingGoalId);

public sealed record ProjectGoalSlotLookup(
    IReadOnlyCollection<ProjectId> ProjectIds,
    GoalEntityType EntityType,
    string EntityId,
    GoalType GoalType,
    string? RankTargetKey = null,
    GoalId? ExcludingGoalId = null);
