using Microsoft.EntityFrameworkCore;
using Npgsql;
using TacticusPlanner.Domain.EventShops;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.EventShopPlans;

public abstract record EventShopPlanWriteResult
{
    private EventShopPlanWriteResult()
    {
    }

    public sealed record Saved(EventShopPlanResponse Plan) : EventShopPlanWriteResult;

    /// <summary>The plan was deleted (or there was none to delete).</summary>
    public sealed record Deleted : EventShopPlanWriteResult;

    /// <param name="Current">The plan as it stands, read after the rejected write.</param>
    public sealed record Conflict(EventShopPlanResponse Current) : EventShopPlanWriteResult;
}

/// <summary>
/// Runs event shop plan writes under the plan revision contract: load the plan row (or none), check
/// <c>expectedRevision</c> (0 for a missing plan), apply the change, and save once — the <c>Revision</c> concurrency
/// token guards the single UPDATE. A stale revision, a concurrency failure at save, or the unique
/// <c>(profile_id, event_id)</c> violation when two callers create the missing plan at once all answer
/// <see cref="EventShopPlanIssueCodes.Stale"/> with a fresh read. See specs/event-shop-plans.
/// </summary>
public sealed class EventShopPlanWriter(PlannerDbContext db)
{
    private const string PlanUniqueIndex = "ix_event_shop_plans_profile_id_event_id";

    /// <summary>Replaces the cart. A cart equal to the stored one (as a set) succeeds without bumping the revision
    /// and, for a missing plan, without creating one.</summary>
    public async Task<EventShopPlanWriteResult> ReplaceEntriesAsync(
        ProfileId profileId,
        GameCatalogEventShopView eventShop,
        long expectedRevision,
        IReadOnlyList<EventShopCartEntry> entries,
        CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        try
        {
            var plan = await db.LoadEventShopPlanAsync(eventShop.Id, tracked: true, ct);
            if ((plan?.Revision ?? 0) != expectedRevision)
                return await ConflictAsync(eventShop.Id, eventShop, ct);

            if (EventShopPlanProjection.SameEntries(plan?.Entries ?? [], [.. entries]))
            {
                return new EventShopPlanWriteResult.Saved(plan is null
                    ? EventShopPlanProjection.Empty(eventShop.Id)
                    : EventShopPlanProjection.Map(plan, eventShop));
            }

            if (plan is null)
            {
                plan = new EventShopPlan
                {
                    Id = EventShopPlanId.From(Guid.CreateVersion7()),
                    ProfileId = profileId,
                    EventId = eventShop.Id,
                    CatalogVersion = GameCatalogRelease.Version,
                };
                db.EventShopPlans.Add(plan);
            }
            else
            {
                db.Entry(plan).Property(entity => entity.UpdatedAt).IsModified = true;
            }

            plan.Entries = [.. entries];
            plan.CatalogVersion = GameCatalogRelease.Version;
            EventShopValidation.AssertInvariants(plan);
            await db.SaveChangesAsync(ct);
            return new EventShopPlanWriteResult.Saved(EventShopPlanProjection.Map(plan, eventShop));
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ConflictAsync(eventShop.Id, eventShop, ct);
        }
        catch (DbUpdateException exception) when (IsPlanUniqueViolation(exception))
        {
            return await ConflictAsync(eventShop.Id, eventShop, ct);
        }
    }

    /// <summary>Deletes the plan. With no stored plan, an expected revision of 0 succeeds and anything else conflicts.</summary>
    public async Task<EventShopPlanWriteResult> DeleteAsync(
        string eventId, GameCatalogEventShopView? eventShop, long expectedRevision, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        try
        {
            var plan = await db.LoadEventShopPlanAsync(eventId, tracked: true, ct);
            if ((plan?.Revision ?? 0) != expectedRevision)
                return await ConflictAsync(eventId, eventShop, ct);

            if (plan is not null)
            {
                db.EventShopPlans.Remove(plan);
                await db.SaveChangesAsync(ct);
            }

            return new EventShopPlanWriteResult.Deleted();
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ConflictAsync(eventId, eventShop, ct);
        }
    }

    private async Task<EventShopPlanWriteResult> ConflictAsync(
        string eventId, GameCatalogEventShopView? eventShop, CancellationToken ct)
    {
        // Read what is committed now.
        db.ChangeTracker.Clear();
        var current = await db.LoadEventShopPlanAsync(eventId, tracked: false, ct);
        return new EventShopPlanWriteResult.Conflict(current is null
            ? EventShopPlanProjection.Empty(eventId)
            : EventShopPlanProjection.Map(current, eventShop));
    }

    /// <summary>Only the plan's own <c>(profile_id, event_id)</c> race is a stale write; any other unique violation is
    /// a bug and is rethrown.</summary>
    private static bool IsPlanUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: PlanUniqueIndex,
        };
}
