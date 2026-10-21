using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Configurations;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>
/// The concurrency boundary of every goal mutation (create, edit, status, membership, order, delete): one
/// profile-row lock, and the in-flight slot rule (<c>project-goal-slots</c>) checked with one query on
/// <c>goals</c>. Membership is not part of the slot: one in-flight goal may belong to any number of projects.
/// </summary>
public sealed class ProjectGoalPlanningService(PlannerDbContext db, GoalOrderService order)
{
    public async Task ExecuteLockedMutationAsync(Func<IDbContextTransaction, Task> mutation, CancellationToken ct)
    {
        // strategy.ExecuteAsync is NOT a working retry here: it re-runs the delegate against the same
        // DbContext without resetting the change tracker, and some call sites write the HTTP response from
        // inside the delegate. It stays only because EF Core requires a user-initiated transaction to be
        // created inside the strategy delegate; the design avoids retryable failures by construction.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // READ COMMITTED, not SERIALIZABLE: the profile row lock below is the mutual-exclusion mechanism, and
            // a waiter, once unblocked, must take a fresh per-statement snapshot that sees what its predecessor
            // committed. Safe only because every conflict- and ordering-relevant read happens after the lock is
            // held — any future read used for such a decision must stay inside the lock.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            // FOR NO KEY UPDATE (its key is never written, so inserts of rows referencing it are not blocked). One
            // statement locks and loads the row tracked, so GoalOrderService reads the order revision as it stands
            // under the lock; an instance tracked earlier would keep stale values, so it is refreshed.
            var alreadyTracked = db.Profiles.Local.Count > 0;
            var profile = await db.LockCurrentProfileAsync(ct);
            if (alreadyTracked)
                await db.Entry(profile).ReloadAsync(ct);

            await mutation(transaction);
        });
    }

    public static ProjectGoal CreateMembership(ProjectId projectId, GoalId goalId, DateTimeOffset now) => new()
    {
        ProjectId = projectId,
        GoalId = goalId,
        CreatedAt = now,
    };

    public static bool IsInFlight(GoalStatus status) => status is GoalStatus.Active or GoalStatus.Paused;

    /// <summary>
    /// The first of <paramref name="candidates"/> (in the order given) whose slot is held by another in-flight goal
    /// of the profile, checked against the in-flight goals the request read under the lock. Candidates are checked
    /// against each other too, so a request cannot put two goals into one slot. Must run under the profile lock.
    /// </summary>
    public async Task<GoalSlotConflict?> FindConflictAsync(IReadOnlyList<Goal> candidates, CancellationToken ct) =>
        candidates.Count == 0 ? null : FindConflict(candidates, await order.InFlightAsync(ct));

    private static GoalSlotConflict? FindConflict(IReadOnlyList<Goal> candidates, IEnumerable<Goal> goals)
    {
        var occupants = goals.Where(goal => IsInFlight(goal.Status) && candidates.All(candidate => candidate.Id != goal.Id)).ToList();
        var claimed = new List<Goal>();
        foreach (var candidate in candidates)
        {
            if (occupants.Concat(claimed).FirstOrDefault(other => SameSlot(other, candidate)) is { } existing)
                return new GoalSlotConflict(candidate, existing);
            claimed.Add(candidate);
        }

        return null;
    }

    private static bool SameSlot(Goal a, Goal b) =>
        a.EntityType == b.EntityType
        && a.EntityId == b.EntityId
        && a.GoalType == b.GoalType
        && RankTargetKey.For(a.GoalType, a.Config) == RankTargetKey.For(b.GoalType, b.Config);

    /// <summary>A race that slipped past <see cref="FindConflictAsync"/> and hit one of the slot indexes.</summary>
    public static bool IsSlotViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
            && postgres.ConstraintName is GoalSlotIndexes.Slot or GoalSlotIndexes.RankTarget;

    /// <summary>After a slot violation: rolls the transaction back and finds, in what is committed now, the goal
    /// holding a candidate's slot.</summary>
    public async Task<GoalSlotConflict> FindConflictAfterFailedSaveAsync(
        IDbContextTransaction transaction, IReadOnlyList<Goal> candidates, DbUpdateException exception, CancellationToken ct)
    {
        await transaction.RollbackAsync(ct);
        await transaction.DisposeAsync();
        db.ChangeTracker.Clear();
        var entityIds = candidates.Select(goal => goal.EntityId).Distinct().ToList();
        var committed = await db.Goals.AsNoTracking()
            .Where(goal => entityIds.Contains(goal.EntityId) && goal.GlobalPriority != null)
            .ToListAsync(ct);
        return FindConflict(candidates, committed)
            ?? throw new InvalidOperationException("A goal slot index failed but no occupying goal was found.", exception);
    }
}

/// <param name="Candidate">The goal that wanted the slot.</param>
/// <param name="Existing">The in-flight goal already holding it.</param>
public sealed record GoalSlotConflict(Goal Candidate, Goal Existing);
