using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace TacticusPlanner.Persistence;

/// <summary>Writes that run under the current profile's row lock.</summary>
public static class ProfileLockedExecution
{
    /// <summary>
    /// Runs <paramref name="mutation"/> in a READ COMMITTED transaction holding the profile row lock, as
    /// <c>ProjectGoalPlanningService.ExecuteLockedMutationAsync</c> does: every count and position read inside it sees
    /// what a predecessor committed, so limits and positions hold under concurrency. The caller must have no unsaved
    /// changes (a rolled-back attempt could not replay them). A retry after a transient failure starts from what is
    /// stored: entities the failed attempt started tracking are detached, and ones the caller was already tracking
    /// stay tracked with their stored values reloaded.
    /// </summary>
    public static async Task<T> ExecuteProfileLockedAsync<T>(
        this PlannerDbContext db, Func<IDbContextTransaction, Task<T>> mutation, CancellationToken ct)
    {
        if (db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Save or discard pending changes before a profile-locked write.");
        var callerTracked = db.ChangeTracker.Entries().Select(entry => entry.Entity)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        var strategy = db.Database.CreateExecutionStrategy();
        var attempt = 0;
        return await strategy.ExecuteAsync(async () =>
        {
            if (attempt++ > 0)
                await ResetAfterFailedAttemptAsync(db, callerTracked, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.LockCurrentProfileAsync(ct);
            return await mutation(transaction);
        });
    }

    private static async Task ResetAfterFailedAttemptAsync(
        PlannerDbContext db, HashSet<object> callerTracked, CancellationToken ct)
    {
        var startedByAttempt = db.ChangeTracker.Entries().Where(entry => !callerTracked.Contains(entry.Entity)).ToList();
        foreach (var entry in startedByAttempt)
            entry.State = EntityState.Detached;

        // A saved but rolled-back change leaves an entity Unchanged with values that were never committed, so every
        // entity the caller tracked is reloaded (which also tracks it again if the failure detached it).
        foreach (var entity in callerTracked)
            await db.Entry(entity).ReloadAsync(ct);
    }
}
