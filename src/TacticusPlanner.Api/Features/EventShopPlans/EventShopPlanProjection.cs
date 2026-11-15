using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.EventShops;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.EventShopPlans;

/// <summary>Loads an event shop plan (one row, the entries are jsonb) and maps it to the single served shape.</summary>
public static class EventShopPlanProjection
{
    /// <summary>Scoped to the caller's profile by <see cref="PlannerDbContext"/>'s global query filter.</summary>
    public static Task<EventShopPlan?> LoadEventShopPlanAsync(
        this PlannerDbContext db, string eventId, bool tracked, CancellationToken ct)
    {
        var plans = tracked ? db.EventShopPlans : db.EventShopPlans.AsNoTracking();
        return plans.FirstOrDefaultAsync(plan => plan.EventId == eventId, ct);
    }

    public static EventShopPlanResponse Empty(string eventId) => new(eventId, 0, null, []);

    /// <param name="eventShop">The event's current catalog view, when it has one: entries follow its day order.</param>
    public static EventShopPlanResponse Map(EventShopPlan plan, GameCatalogEventShopView? eventShop)
    {
        var dayOrder = EventShopPlanCatalogValidator.DayOrder(eventShop);
        return new EventShopPlanResponse(
            plan.EventId,
            plan.Revision,
            plan.CatalogVersion,
            plan.Entries
                .OrderBy(entry => entry.Week)
                .ThenBy(entry => IndexOf(dayOrder, entry.Day))
                .ThenBy(entry => entry.SlotIndex)
                .Select(entry => new EventShopCartEntryResponse(
                    entry.Week, entry.Day, entry.SlotIndex, entry.RewardType, entry.Quantity, entry.PurchasedQuantity,
                    entry.DraftAlliance))
                .ToArray());
    }

    public static EventShopCartEntry ToEntity(EventShopCartEntryRequest entry) =>
        new()
        {
            Week = entry.Week,
            Day = entry.Day!,
            SlotIndex = entry.SlotIndex,
            RewardType = entry.RewardType!,
            Quantity = entry.Quantity,
            PurchasedQuantity = entry.PurchasedQuantity,
            DraftAlliance = entry.DraftAlliance,
        };

    /// <summary>Whether two carts hold the same entries, in any order.</summary>
    public static bool SameEntries(IReadOnlyCollection<EventShopCartEntry> stored, IReadOnlyCollection<EventShopCartEntry> requested) =>
        stored.Count == requested.Count
        && stored.Select(Key).ToHashSet().SetEquals(requested.Select(Key));

    private static (int, string, int, string, int, int, string?) Key(EventShopCartEntry entry) =>
        (entry.Week, entry.Day, entry.SlotIndex, entry.RewardType, entry.Quantity, entry.PurchasedQuantity, entry.DraftAlliance);

    private static int IndexOf(IReadOnlyList<string> dayOrder, string day)
    {
        for (var index = 0; index < dayOrder.Count; index++)
        {
            if (string.Equals(dayOrder[index], day, StringComparison.Ordinal))
                return index;
        }

        return dayOrder.Count;
    }
}
