using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

/// <summary>What a plan mutation did to the loaded (or newly created) plan.</summary>
public enum LegendaryEventPlanMutation
{
    /// <summary>The plan changed: save it and bump the revision.</summary>
    Applied,

    /// <summary>Nothing to change (a reorder to the current order): answer with the plan, revision kept.</summary>
    Unchanged,

    /// <summary>The route named a team that is not on this plan.</summary>
    TeamNotFound,

    /// <summary>A reorder whose ids are not exactly the lane's team set.</summary>
    OrderSetMismatch,
}

public abstract record LegendaryEventPlanWriteResult
{
    private LegendaryEventPlanWriteResult()
    {
    }

    public sealed record Saved(LegendaryEventPlanResponse Plan) : LegendaryEventPlanWriteResult;

    public sealed record Conflict(LegendaryEventPlanConflictResponse Body) : LegendaryEventPlanWriteResult;

    public sealed record TeamNotFound : LegendaryEventPlanWriteResult;
}

/// <summary>Context handed to a mutation: the tracked plan plus a way to flush row removals before the rows that
/// replace them are added (members and objectives are keyed by position/index, so a replacement row can reuse a
/// removed row's key or swap units between positions under the unique <c>(team_id, unit_id)</c> index).</summary>
public sealed class LegendaryEventPlanMutationContext(PlannerDbContext db, LegendaryEventPlan plan, DateTimeOffset now)
{
    public LegendaryEventPlan Plan { get; } = plan;

    public DateTimeOffset Now { get; } = now;

    public PlannerDbContext Db { get; } = db;

    /// <summary>Saves pending removals without touching the plan row (no revision bump); the final save does
    /// that. Runs inside the writer's transaction, so a later conflict rolls these back too.</summary>
    public Task FlushRemovalsAsync(CancellationToken ct) => Db.SaveChangesAsync(ct);
}

/// <summary>
/// Runs one plan mutation under the plan-level revision contract (design D4): load (or lazily create) the plan,
/// check <c>expectedRevision</c> (0 for a missing plan), apply, pin <c>catalogVersion</c>, mark the plan
/// modified so <c>EntityMetadataInterceptor</c> bumps its revision, save and commit. A stale revision, a
/// concurrency failure at save, or the unique <c>(profile_id, event_id)</c> violation when two callers create the
/// missing plan at once all answer <c>legendaryEventPlanStale</c> with a fresh read of the current plan.
/// </summary>
public sealed class LegendaryEventPlanWriter(PlannerDbContext db, TimeProvider timeProvider)
{
    public async Task<LegendaryEventPlanWriteResult> WriteAsync(
        ProfileId profileId,
        string eventId,
        long expectedRevision,
        Func<LegendaryEventPlanMutationContext, CancellationToken, Task<LegendaryEventPlanMutation>> mutate,
        CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        var outcome = await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
                : null;
            try
            {
                var plan = await db.LoadPlanAsync(eventId, tracked: true, ct);
                if ((plan?.Revision ?? 0) != expectedRevision)
                    return Outcome.Stale(LegendaryEventPlanIssueCodes.Stale);

                var created = plan is null;
                plan ??= NewPlan(profileId, eventId);
                if (created)
                    db.LegendaryEventPlans.Add(plan);

                var mutation = await mutate(new LegendaryEventPlanMutationContext(db, plan, timeProvider.GetUtcNow()), ct);
                switch (mutation)
                {
                    case LegendaryEventPlanMutation.TeamNotFound:
                        return Outcome.NotFound;
                    case LegendaryEventPlanMutation.OrderSetMismatch:
                        return Outcome.Stale(LegendaryEventPlanIssueCodes.OrderSetMismatch);
                    case LegendaryEventPlanMutation.Unchanged:
                        return Outcome.Done(created ? LegendaryEventPlanProjection.Empty(eventId) : LegendaryEventPlanProjection.Map(plan));
                }

                plan.CatalogVersion = GameCatalogRelease.Version;
                if (!created)
                    db.Entry(plan).Property(entity => entity.UpdatedAt).IsModified = true;
                await db.SaveChangesAsync(ct);
                if (transaction is not null)
                    await transaction.CommitAsync(ct);
                return Outcome.Done(LegendaryEventPlanProjection.Map(plan));
            }
            catch (DbUpdateConcurrencyException)
            {
                return Outcome.Stale(LegendaryEventPlanIssueCodes.Stale);
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                return Outcome.Stale(LegendaryEventPlanIssueCodes.Stale);
            }
        });

        if (outcome.Plan is { } saved)
            return new LegendaryEventPlanWriteResult.Saved(saved);
        if (outcome.IssueCode is null)
            return new LegendaryEventPlanWriteResult.TeamNotFound();

        // Outside the (rolled back) transaction: read what is committed now.
        db.ChangeTracker.Clear();
        var current = await db.LoadPlanAsync(eventId, tracked: false, ct);
        var body = current is null ? LegendaryEventPlanProjection.Empty(eventId) : LegendaryEventPlanProjection.Map(current);
        return new LegendaryEventPlanWriteResult.Conflict(new LegendaryEventPlanConflictResponse(
            outcome.IssueCode,
            outcome.IssueCode == LegendaryEventPlanIssueCodes.OrderSetMismatch
                ? "The lane's teams changed since they were loaded. Review the current order and try again."
                : "The plan changed since it was loaded. Review the current plan and try again.",
            body));
    }

    private static LegendaryEventPlan NewPlan(ProfileId profileId, string eventId) =>
        new()
        {
            Id = LegendaryEventPlanId.From(Guid.NewGuid()),
            ProfileId = profileId,
            EventId = eventId,
            CatalogVersion = GameCatalogRelease.Version,
        };

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private sealed record Outcome(LegendaryEventPlanResponse? Plan, string? IssueCode)
    {
        public static readonly Outcome NotFound = new(null, null);

        public static Outcome Done(LegendaryEventPlanResponse plan) => new(plan, null);

        public static Outcome Stale(string issueCode) => new(null, issueCode);
    }
}
