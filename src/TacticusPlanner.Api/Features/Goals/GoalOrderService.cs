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
/// inside one transaction. The in-flight goals are read once per request (tracked) and every order and slot
/// decision of that request works on them.
/// </summary>
public sealed class GoalOrderService(PlannerDbContext db)
{
    private List<Goal>? inFlight;
    private readonly List<Goal> appended = [];
    private int? nextPosition;
    private bool changed;
    private bool released;

    /// <summary>The goals that were in flight when this request first asked (tracked, so later in-memory
    /// changes show); read once under the lock.</summary>
    public async Task<IReadOnlyList<Goal>> InFlightAsync(CancellationToken ct) =>
        inFlight ??= await db.Goals
            .Where(entity => entity.GlobalPriority != null)
            .OrderBy(entity => entity.GlobalPriority)
            .ToListAsync(ct);

    /// <summary>The order as it stands in memory: the in-flight goals plus this request's appends, by position.</summary>
    private async Task<List<Goal>> OrderedAsync(CancellationToken ct) =>
        (await InFlightAsync(ct)).Concat(appended)
            .Where(goal => goal.GlobalPriority is not null)
            .Distinct()
            .OrderBy(goal => goal.GlobalPriority)
            .ToList();

    /// <summary>Puts a newly in-flight goal (creation, or a terminal goal returning to Active/Paused) at the
    /// end of the order. Calls append in call order.</summary>
    public async Task AppendAsync(Goal goal, CancellationToken ct)
    {
        nextPosition ??= ((await OrderedAsync(ct)).LastOrDefault()?.GlobalPriority ?? 0) + 1;
        goal.GlobalPriority = nextPosition++;
        appended.Add(goal);
        changed = true;
    }

    /// <summary>Takes a goal out of the order (completed/archived/deleted); positions compact in
    /// <see cref="CompleteAsync"/>.</summary>
    public void Release(Goal goal)
    {
        if (goal.GlobalPriority is null) return;
        goal.GlobalPriority = null;
        changed = true;
        released = true;
    }

    /// <summary>Re-densifies the order, advances the order revision when this request touched the in-flight set,
    /// and persists every pending change in one save. Call before committing the transaction.</summary>
    public async Task CompleteAsync(CancellationToken ct)
    {
        if (changed)
        {
            // Appends keep the order dense; only a released goal leaves a gap to close.
            if (released)
                Renumber(await OrderedAsync(ct));
            LockedProfile.GoalOrderRevision++;
        }

        await db.SaveChangesAsync(ct);
        changed = false;
        released = false;
        nextPosition = null;
    }

    /// <summary>The profile as it stands under the lock: <see cref="Projects.ProjectGoalPlanningService"/>
    /// loads it (tracked) when it takes the row lock, so no further query is needed.</summary>
    private Domain.Profiles.Profile LockedProfile => db.Profiles.Local.SingleOrDefault()
        ?? throw new InvalidOperationException(
            "The goal order can only change inside ProjectGoalPlanningService.ExecuteLockedMutationAsync.");

    /// <summary>The current revision and in-flight goal ids in order.</summary>
    public async Task<GoalOrderSnapshot> ReadAsync(CancellationToken ct) =>
        new(LockedProfile.GoalOrderRevision, (await OrderedAsync(ct)).Select(goal => goal.Id).ToList());

    /// <summary>Replaces the order with <paramref name="requested"/>, which must be exactly the current
    /// in-flight goals (no missing, added, duplicate, foreign or terminal ids) at the expected revision.
    /// Dependency order is deliberately not checked.</summary>
    public async Task<GoalOrderResult> ReorderAsync(
        IReadOnlyList<GoalId> requested, long expectedRevision, CancellationToken ct)
    {
        var profile = LockedProfile;
        var ordered = await OrderedAsync(ct);
        GoalOrderOutcome? rejection = null;
        if (expectedRevision != profile.GoalOrderRevision)
            rejection = GoalOrderOutcome.StaleRevision;
        else if (requested.Distinct().Count() != requested.Count)
            rejection = GoalOrderOutcome.DuplicateGoal;
        else if (requested.Count != ordered.Count || requested.Any(id => ordered.All(goal => goal.Id != id)))
            rejection = GoalOrderOutcome.SetMismatch;
        if (rejection is { } outcome) return Failure(outcome, profile, ordered);

        var byId = ordered.ToDictionary(goal => goal.Id);
        return await CommitAsync(profile, ordered, requested.Select(id => byId[id]).ToList(), save: true, ct);
    }

    /// <summary>Moves <paramref name="goalId"/> to the position <paramref name="displacedGoalId"/> holds;
    /// goals in between shift one place toward the vacated slot and every other relative order is kept (an
    /// array move). Membership of the goals in a project is the caller's check.</summary>
    public async Task<GoalOrderResult> MoveAsync(
        GoalId goalId, GoalId displacedGoalId, long expectedRevision, CancellationToken ct)
    {
        var profile = LockedProfile;
        var ordered = await OrderedAsync(ct);
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
        return await CommitAsync(profile, ordered, moved, save: true, ct);
    }

    /// <summary>Moves <paramref name="goalId"/> to the 1-based <paramref name="position"/> in the in-flight
    /// order (the same array move as <see cref="MoveAsync"/>). Unlike the other methods it does not save:
    /// the caller's single <c>SaveChangesAsync</c> persists the new positions and the revision bump, so a
    /// combined edit stays one write.</summary>
    public async Task<GoalOrderResult> MoveToPositionAsync(
        GoalId goalId, int position, long expectedRevision, CancellationToken ct)
    {
        var profile = LockedProfile;
        var ordered = await OrderedAsync(ct);
        var from = ordered.FindIndex(goal => goal.Id == goalId);
        GoalOrderOutcome? rejection = null;
        if (expectedRevision != profile.GoalOrderRevision)
            rejection = GoalOrderOutcome.StaleRevision;
        else if (from < 0)
            rejection = GoalOrderOutcome.NotInFlight;
        else if (position < 1 || position > ordered.Count)
            rejection = GoalOrderOutcome.PositionOutOfRange;
        if (rejection is { } outcome) return Failure(outcome, profile, ordered);

        var moved = ordered.ToList();
        var goal = moved[from];
        moved.RemoveAt(from);
        moved.Insert(position - 1, goal);
        return await CommitAsync(profile, ordered, moved, save: false, ct);
    }

    private static GoalOrderResult Failure(GoalOrderOutcome outcome, Domain.Profiles.Profile profile, List<Goal> ordered) =>
        new(outcome, new GoalOrderSnapshot(profile.GoalOrderRevision, ordered.Select(goal => goal.Id).ToList()));

    private async Task<GoalOrderResult> CommitAsync(
        Domain.Profiles.Profile profile, List<Goal> before, List<Goal> after, bool save, CancellationToken ct)
    {
        if (!after.SequenceEqual(before))
        {
            Renumber(after);
            profile.GoalOrderRevision++;
            if (save)
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
}

public enum GoalOrderOutcome
{
    Ok,
    StaleRevision,
    SetMismatch,
    DuplicateGoal,
    SameGoal,
    NotInFlight,
    PositionOutOfRange,
}

public sealed record GoalOrderSnapshot(long Revision, IReadOnlyList<GoalId> GoalIds);

public sealed record GoalOrderResult(GoalOrderOutcome Outcome, GoalOrderSnapshot Order);
