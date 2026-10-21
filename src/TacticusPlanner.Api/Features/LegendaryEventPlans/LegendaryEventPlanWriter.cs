using Microsoft.EntityFrameworkCore;
using Npgsql;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog;
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

    /// <summary>The fields fail validation against the loaded plan; the mutation reports why itself.</summary>
    Rejected,
}

public abstract record LegendaryEventPlanWriteResult
{
    private LegendaryEventPlanWriteResult()
    {
    }

    public sealed record Saved(LegendaryEventPlanResponse Plan) : LegendaryEventPlanWriteResult;

    /// <param name="Current">The plan as it stands, read after the rejected write.</param>
    public sealed record Conflict(string IssueCode, string Message, LegendaryEventPlanResponse Current)
        : LegendaryEventPlanWriteResult;

    public sealed record TeamNotFound : LegendaryEventPlanWriteResult;

    public sealed record Rejected : LegendaryEventPlanWriteResult;
}

/// <summary>Context handed to a mutation: the loaded (tracked) plan aggregate and the write time.</summary>
public sealed record LegendaryEventPlanMutationContext(LegendaryEventPlan Plan, DateTimeOffset Now);

/// <summary>
/// Runs one plan mutation under the plan-level revision contract (design D4): load (or lazily create) the plan
/// row, check <c>expectedRevision</c> (0 for a missing plan), apply the mutation in memory, assert the plan's
/// invariants, pin <c>catalogVersion</c>, mark the plan modified so <c>EntityMetadataInterceptor</c> bumps its
/// revision, and save once — the <c>Revision</c> concurrency token guards the single UPDATE, so no transaction is
/// needed. A stale revision, a concurrency failure at save, or the unique <c>(profile_id, event_id)</c> violation
/// when two callers create the missing plan at once all answer <c>legendaryEventPlanStale</c> with a fresh read.
/// </summary>
public sealed class LegendaryEventPlanWriter(
    PlannerDbContext db, TimeProvider timeProvider, IGameCatalogProvider? catalog = null)
{
    public async Task<LegendaryEventPlanWriteResult> WriteAsync(
        ProfileId profileId,
        string eventId,
        long expectedRevision,
        Func<LegendaryEventPlanMutationContext, CancellationToken, Task<LegendaryEventPlanMutation>> mutate,
        CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var outcome = await ApplyAsync(profileId, eventId, expectedRevision, mutate, ct);

        if (outcome.Plan is { } saved)
            return new LegendaryEventPlanWriteResult.Saved(saved);
        if (outcome.Rejected)
            return new LegendaryEventPlanWriteResult.Rejected();
        if (outcome.IssueCode is null)
            return new LegendaryEventPlanWriteResult.TeamNotFound();

        // Read what is committed now.
        db.ChangeTracker.Clear();
        var current = await db.LoadPlanAsync(eventId, tracked: false, ct);
        var body = current is null ? LegendaryEventPlanProjection.Empty(eventId) : LegendaryEventPlanProjection.Map(current, CatalogEvent(eventId));
        return new LegendaryEventPlanWriteResult.Conflict(
            outcome.IssueCode,
            outcome.IssueCode == LegendaryEventPlanIssueCodes.OrderSetMismatch
                ? "The lane's teams changed since they were loaded. Review the current order and try again."
                : "The plan changed since it was loaded. Review the current plan and try again.",
            body);
    }

    private async Task<Outcome> ApplyAsync(
        ProfileId profileId,
        string eventId,
        long expectedRevision,
        Func<LegendaryEventPlanMutationContext, CancellationToken, Task<LegendaryEventPlanMutation>> mutate,
        CancellationToken ct)
    {
        try
        {
            var plan = await db.LoadPlanAsync(eventId, tracked: true, ct);
            if ((plan?.Revision ?? 0) != expectedRevision)
                return Outcome.Stale(LegendaryEventPlanIssueCodes.Stale);

            var created = plan is null;
            plan ??= NewPlan(profileId, eventId);
            if (created)
                db.LegendaryEventPlans.Add(plan);

            var mutation = await mutate(new LegendaryEventPlanMutationContext(plan, timeProvider.GetUtcNow()), ct);
            switch (mutation)
            {
                case LegendaryEventPlanMutation.TeamNotFound:
                    return Outcome.NotFound;
                case LegendaryEventPlanMutation.Rejected:
                    return Outcome.Invalid;
                case LegendaryEventPlanMutation.OrderSetMismatch:
                    return Outcome.Stale(LegendaryEventPlanIssueCodes.OrderSetMismatch);
                case LegendaryEventPlanMutation.Unchanged:
                    return Outcome.Done(created ? LegendaryEventPlanProjection.Empty(eventId) : LegendaryEventPlanProjection.Map(plan, CatalogEvent(eventId)));
            }

            LegendaryEventValidation.AssertInvariants(plan);
            plan.CatalogVersion = GameCatalogRelease.Version;
            if (!created)
                db.Entry(plan).Property(entity => entity.UpdatedAt).IsModified = true;
            await db.SaveChangesAsync(ct);
            return Outcome.Done(LegendaryEventPlanProjection.Map(plan, CatalogEvent(eventId)));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Outcome.Stale(LegendaryEventPlanIssueCodes.Stale);
        }
        catch (DbUpdateException exception) when (IsPlanUniqueViolation(exception))
        {
            return Outcome.Stale(LegendaryEventPlanIssueCodes.Stale);
        }
    }

    private GameCatalogLreView? CatalogEvent(string eventId) =>
        catalog?.Current.LreViews.FirstOrDefault(view => view.Id == eventId);

    private static LegendaryEventPlan NewPlan(ProfileId profileId, string eventId) =>
        new()
        {
            Id = LegendaryEventPlanId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            EventId = eventId,
            CatalogVersion = GameCatalogRelease.Version,
        };

    private const string PlanUniqueIndex = "ix_legendary_event_plans_profile_id_event_id";

    /// <summary>Only the plan's own <c>(profile_id, event_id)</c> race is a stale write; any other unique
    /// violation is a bug and is rethrown.</summary>
    private static bool IsPlanUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: PlanUniqueIndex,
        };

    private sealed record Outcome(LegendaryEventPlanResponse? Plan, string? IssueCode, bool Rejected = false)
    {
        public static readonly Outcome NotFound = new(null, null);

        public static readonly Outcome Invalid = new(null, null, Rejected: true);

        public static Outcome Done(LegendaryEventPlanResponse plan) => new(plan, null);

        public static Outcome Stale(string issueCode) => new(null, issueCode);
    }
}
