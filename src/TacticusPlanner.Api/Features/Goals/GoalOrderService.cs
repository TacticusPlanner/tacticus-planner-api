using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// The single writer of <see cref="Goal.GlobalPriority"/> and <c>Profile.GoalOrderRevision</c>: one
/// account-wide order of Active/Paused goals (spec: <c>global-goal-priority</c>). Every method must run
/// inside <see cref="Projects.ProjectGoalPlanningService.ExecuteLockedMutationAsync"/>, which serializes
/// the account's mutations on its Profile row, so reads here are consistent with the writes that follow.
/// Positions are always dense (1..N); the unique constraint is deferred, so a reorder can permute them
/// inside one transaction.
/// </summary>
public sealed class GoalOrderService(PlannerDbContext db)
{
    private int? nextPosition;
    private bool changed;

    /// <summary>Puts a newly in-flight goal (creation, or a terminal goal returning to Active/Paused) at the
    /// end of the order. Calls append in call order.</summary>
    public async Task AppendAsync(Goal goal, CancellationToken ct)
    {
        nextPosition ??= (await db.Goals.MaxAsync(entity => entity.GlobalPriority, ct) ?? 0) + 1;
        goal.GlobalPriority = nextPosition++;
        changed = true;
    }

    /// <summary>Takes a goal out of the order (completed/archived/deleted); positions compact in
    /// <see cref="CompleteAsync"/>.</summary>
    public void Release(Goal goal)
    {
        if (goal.GlobalPriority is null) return;
        goal.GlobalPriority = null;
        changed = true;
    }

    /// <summary>Persists pending changes, re-densifies the order and advances the order revision — a no-op
    /// when nothing this request did touched the in-flight set. Call before committing the transaction.</summary>
    public async Task CompleteAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        if (!changed) return;

        var ordered = await LoadInFlightAsync(ct);
        Renumber(ordered);
        var profile = await LoadProfileAsync(ct);
        profile.GoalOrderRevision++;
        await db.SaveChangesAsync(ct);
        changed = false;
        nextPosition = null;
    }

    /// <summary>The profile as it stands under the lock. Callers such as <c>EnsureDefaultProjectAsync</c> load
    /// it (tracked) before the lock is taken, and EF would hand back that stale instance, so the revision
    /// would be incremented from a value read before a concurrent reorder committed.</summary>
    private async Task<Domain.Profiles.Profile> LoadProfileAsync(CancellationToken ct)
    {
        var profile = await db.Profiles.FirstAsync(ct);
        await db.Entry(profile).ReloadAsync(ct);
        return profile;
    }

    /// <summary>The current revision and in-flight goal ids in order.</summary>
    public async Task<GoalOrderSnapshot> ReadAsync(CancellationToken ct)
    {
        var revision = (await db.Profiles.AsNoTracking().FirstAsync(ct)).GoalOrderRevision;
        var ids = await db.Goals.AsNoTracking()
            .Where(entity => entity.GlobalPriority != null)
            .OrderBy(entity => entity.GlobalPriority)
            .Select(entity => entity.Id)
            .ToListAsync(ct);
        return new GoalOrderSnapshot(revision, ids);
    }

    /// <summary>Replaces the order with <paramref name="requested"/>, which must be exactly the current
    /// in-flight goals (no missing, added, duplicate, foreign or terminal ids) at the expected revision.
    /// Dependency order is deliberately not checked.</summary>
    public async Task<GoalOrderResult> ReorderAsync(
        IReadOnlyList<GoalId> requested, long expectedRevision, CancellationToken ct)
    {
        var profile = await LoadProfileAsync(ct);
        var ordered = await LoadInFlightAsync(ct);
        GoalOrderOutcome? rejection = null;
        if (expectedRevision != profile.GoalOrderRevision)
            rejection = GoalOrderOutcome.StaleRevision;
        else if (requested.Distinct().Count() != requested.Count)
            rejection = GoalOrderOutcome.DuplicateGoal;
        else if (requested.Count != ordered.Count || requested.Any(id => ordered.All(goal => goal.Id != id)))
            rejection = GoalOrderOutcome.SetMismatch;
        if (rejection is { } outcome) return Failure(outcome, profile, ordered);

        var byId = ordered.ToDictionary(goal => goal.Id);
        return await CommitAsync(profile, ordered, requested.Select(id => byId[id]).ToList(), ct);
    }

    /// <summary>Moves <paramref name="goalId"/> to the position <paramref name="displacedGoalId"/> holds;
    /// goals in between shift one place toward the vacated slot and every other relative order is kept (an
    /// array move). Membership of the goals in a project is the caller's check.</summary>
    public async Task<GoalOrderResult> MoveAsync(
        GoalId goalId, GoalId displacedGoalId, long expectedRevision, CancellationToken ct)
    {
        var profile = await LoadProfileAsync(ct);
        var ordered = await LoadInFlightAsync(ct);
        var from = ordered.FindIndex(goal => goal.Id == goalId);
        var to = ordered.FindIndex(goal => goal.Id == displacedGoalId);
        GoalOrderOutcome? rejection = null;
        if (expectedRevision != profile.GoalOrderRevision)
            rejection = GoalOrderOutcome.StaleRevision;
        else if (goalId == displacedGoalId)
            rejection = GoalOrderOutcome.SameGoal;
        else if (from < 0 || to < 0)
            rejection = GoalOrderOutcome.SetMismatch;
        if (rejection is { } outcome) return Failure(outcome, profile, ordered);

        var moved = ordered.ToList();
        var goal = moved[from];
        moved.RemoveAt(from);
        moved.Insert(to, goal);
        return await CommitAsync(profile, ordered, moved, ct);
    }

    private static GoalOrderResult Failure(GoalOrderOutcome outcome, Domain.Profiles.Profile profile, List<Goal> ordered) =>
        new(outcome, new GoalOrderSnapshot(profile.GoalOrderRevision, ordered.Select(goal => goal.Id).ToList()));

    private async Task<GoalOrderResult> CommitAsync(
        Domain.Profiles.Profile profile, List<Goal> before, List<Goal> after, CancellationToken ct)
    {
        if (!after.SequenceEqual(before))
        {
            Renumber(after);
            profile.GoalOrderRevision++;
            await db.SaveChangesAsync(ct);
        }

        return new GoalOrderResult(
            GoalOrderOutcome.Ok, new GoalOrderSnapshot(profile.GoalOrderRevision, after.Select(goal => goal.Id).ToList()));
    }

    private static void Renumber(List<Goal> ordered)
    {
        for (var index = 0; index < ordered.Count; index++)
            ordered[index].GlobalPriority = index + 1;
    }

    private Task<List<Goal>> LoadInFlightAsync(CancellationToken ct) =>
        db.Goals
            .Where(entity => entity.GlobalPriority != null)
            .OrderBy(entity => entity.GlobalPriority)
            .ToListAsync(ct);
}

public enum GoalOrderOutcome
{
    Ok,
    StaleRevision,
    SetMismatch,
    DuplicateGoal,
    SameGoal,
}

public sealed record GoalOrderSnapshot(long Revision, IReadOnlyList<GoalId> GoalIds);

public sealed record GoalOrderResult(GoalOrderOutcome Outcome, GoalOrderSnapshot Order);
