using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>
/// The concurrency boundary of every goal mutation (create, edit, status, membership, order, delete, V1
/// import): one profile-row lock, and the in-flight slot rule (<c>project-goal-slots</c>) checked with one query
/// on <c>goals</c>. No database index backs the rule, so a goal write that skips the lock or
/// <see cref="FindConflictAsync"/> can create duplicates. Membership is not part of the slot: one in-flight goal
/// may belong to any number of projects.
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

    /// <summary>The profile's goals outside Archived, per goal type: what <see cref="GoalValidation.MaxGoalsPerType"/>
    /// caps. Reads the database, not tracked changes. Must run under the profile lock.</summary>
    public async Task<Dictionary<GoalType, int>> CappedGoalCountsAsync(CancellationToken ct) =>
        await db.Goals
            .Where(goal => goal.Status != GoalStatus.Archived)
            .GroupBy(goal => goal.GoalType)
            .Select(group => new { GoalType = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.GoalType, entry => entry.Count, ct);

    /// <summary>The first goal type that would go over <see cref="GoalValidation.MaxGoalsPerType"/> if
    /// <paramref name="adding"/> (goals entering the non-Archived set) joined the profile; null when all fit. Must
    /// run under the profile lock.</summary>
    public async Task<GoalType?> FindGoalLimitExceededAsync(IReadOnlyCollection<Goal> adding, CancellationToken ct)
    {
        if (adding.Count == 0)
            return null;

        var counts = await CappedGoalCountsAsync(ct);
        return adding.GroupBy(goal => goal.GoalType)
            .Where(group => counts.GetValueOrDefault(group.Key) + group.Count() > GoalValidation.MaxGoalsPerType)
            .Select(group => (GoalType?)group.Key)
            .FirstOrDefault();
    }

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
        && GoalTargetKey.For(a) == GoalTargetKey.For(b);
}

/// <param name="Candidate">The goal that wanted the slot.</param>
/// <param name="Existing">The in-flight goal already holding it.</param>
public sealed record GoalSlotConflict(Goal Candidate, Goal Existing);
