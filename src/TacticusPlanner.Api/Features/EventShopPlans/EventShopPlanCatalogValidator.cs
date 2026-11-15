using TacticusPlanner.Domain.EventShops;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Features.EventShopPlans;

/// <summary>
/// Validates an event shop cart against the current catalog <c>event-shops</c> dataset: each entry must name a week,
/// day and slot of the event, a reward that slot offers that day, and a quantity within that variant's per-day cap.
/// Lock ids and power-level bounds are ignored: they depend on the roster and are resolved by the client. See
/// specs/event-shop-plans.
/// </summary>
public sealed class EventShopPlanCatalogValidator(IGameCatalogProvider catalog)
{
    public GameCatalogEventShopView? FindEvent(string eventId) =>
        catalog.Current.EventShopViews.FirstOrDefault(view => string.Equals(view.Id, eventId, StringComparison.Ordinal));

    public static IReadOnlyList<EventShopFieldFailure> ValidateEntries(
        GameCatalogEventShopView eventShop, IReadOnlyList<EventShopCartEntryRequest>? entries)
    {
        if (entries is null)
            return [new("entries", "entries is required.")];

        if (entries.Count > EventShopValidation.MaxEntries)
            return [new("entries", $"A plan holds at most {EventShopValidation.MaxEntries} entries.")];

        var failures = new List<EventShopFieldFailure>();
        var keys = new HashSet<(int, string, int)>();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null)
            {
                failures.Add(new($"entries[{index}]", "An entry is required."));
                continue;
            }

            var before = failures.Count;
            ValidateEntry(eventShop, entry, $"entries[{index}]", failures);
            if (failures.Count == before && !keys.Add((entry.Week, entry.Day!, entry.SlotIndex)))
            {
                failures.Add(new($"entries[{index}]",
                    $"Another entry already plans week {entry.Week} {entry.Day} slot {entry.SlotIndex}."));
            }
        }

        return failures;
    }

    private static void ValidateEntry(
        GameCatalogEventShopView eventShop, EventShopCartEntryRequest entry, string field, List<EventShopFieldFailure> failures)
    {
        if (entry.Week < 1 || entry.Week > eventShop.Weeks.Count)
        {
            failures.Add(new($"{field}.week", $"week must be 1..{eventShop.Weeks.Count}."));
            return;
        }

        if (entry.Day is null || !EventShopValidation.Days.Contains(entry.Day))
        {
            failures.Add(new($"{field}.day", "day must be MON, TUE, WED, THU, FRI, SAT or SUN."));
            return;
        }

        var slots = eventShop.Weeks[entry.Week - 1].Slots;
        if (entry.SlotIndex < 0 || entry.SlotIndex >= slots.Count)
        {
            failures.Add(new($"{field}.slotIndex", $"slotIndex must be 0..{slots.Count - 1} for week {entry.Week}."));
            return;
        }

        var variants = slots[entry.SlotIndex].Variants
            .Where(variant => string.Equals(variant.Reward.Type, entry.RewardType, StringComparison.Ordinal)
                && variant.Days.Contains(entry.Day))
            .ToArray();
        if (variants.Length == 0)
        {
            failures.Add(new($"{field}.rewardType",
                $"Week {entry.Week} slot {entry.SlotIndex} does not offer '{entry.RewardType}' on {entry.Day}."));
            return;
        }

        // Variants of one reward on one day differ only by lock / power level; the largest cap is the one a player
        // can meet.
        var cap = variants.Max(variant => variant.MaxPurchasesPerDay);
        if (entry.Quantity < 1 || entry.Quantity > cap)
            failures.Add(new($"{field}.quantity", $"quantity must be 1..{cap}."));
        else if (entry.PurchasedQuantity < 0 || entry.PurchasedQuantity > entry.Quantity)
            failures.Add(new($"{field}.purchasedQuantity", "purchasedQuantity must be 0..quantity."));

        if (entry.DraftAlliance is not null
            && (!entry.RewardType!.StartsWith(EventShopValidation.DraftRewardPrefix, StringComparison.Ordinal)
                || !EventShopValidation.Alliances.Contains(entry.DraftAlliance)))
        {
            failures.Add(new($"{field}.draftAlliance",
                "draftAlliance is only for a draft_ reward and must be imperial, xenos or chaos."));
        }
    }

    /// <summary>
    /// The event's day order, starting at the weekday of its <c>startUtc</c> (an event need not start on a Monday);
    /// Monday-first for an event no longer in the catalog.
    /// </summary>
    public static IReadOnlyList<string> DayOrder(GameCatalogEventShopView? eventShop)
    {
        if (eventShop is null)
            return EventShopValidation.Days;

        // DayOfWeek is Sunday = 0; Days is Monday-first.
        var start = ((int)eventShop.StartUtc.UtcDateTime.DayOfWeek + 6) % 7;
        return Enumerable.Range(0, 7).Select(offset => EventShopValidation.Days[(start + offset) % 7]).ToArray();
    }
}
