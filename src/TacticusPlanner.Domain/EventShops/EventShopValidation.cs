namespace TacticusPlanner.Domain.EventShops;

/// <summary>Limits and shape invariants of an <see cref="EventShopPlan"/>. See specs/event-shop-plans.</summary>
public static class EventShopValidation
{
    public const int MaxEventIdLength = 128;

    public const int MaxCatalogVersionLength = 64;

    public const int MaxEntries = 1000;

    public const string DraftRewardPrefix = "draft_";

    public static readonly IReadOnlyList<string> Days = ["MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"];

    public static readonly IReadOnlyList<string> Alliances = ["imperial", "xenos", "chaos"];

    /// <summary>
    /// The invariants a stored plan must always meet regardless of the catalog: entry count, day and alliance
    /// vocabulary, non-negative positions, <c>0 ≤ purchased ≤ quantity</c>, a draft alliance only on a draft reward,
    /// and at most one entry per (week, day, slot). Throws on a violation: callers validate requests first, so this
    /// only fires on a programming error.
    /// </summary>
    public static void AssertInvariants(EventShopPlan plan)
    {
        if (plan.Entries.Count > MaxEntries)
        {
            throw new InvalidOperationException($"Event shop plan '{plan.EventId}' has more than {MaxEntries} entries.");
        }

        var keys = new HashSet<(int, string, int)>();
        foreach (var entry in plan.Entries)
        {
            var valid = entry.Week >= 1
                && Days.Contains(entry.Day)
                && entry.SlotIndex >= 0
                && !string.IsNullOrEmpty(entry.RewardType)
                && entry.Quantity >= 1
                && entry.PurchasedQuantity >= 0
                && entry.PurchasedQuantity <= entry.Quantity
                && (entry.DraftAlliance is null
                    || (entry.RewardType.StartsWith(DraftRewardPrefix, StringComparison.Ordinal)
                        && Alliances.Contains(entry.DraftAlliance)));

            if (!valid || !keys.Add((entry.Week, entry.Day, entry.SlotIndex)))
            {
                throw new InvalidOperationException(
                    $"Event shop plan '{plan.EventId}' has an invalid or duplicate entry (week {entry.Week}, {entry.Day}, slot {entry.SlotIndex}).");
            }
        }
    }
}
