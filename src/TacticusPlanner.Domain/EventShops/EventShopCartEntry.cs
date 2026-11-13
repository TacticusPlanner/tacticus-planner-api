namespace TacticusPlanner.Domain.EventShops;

/// <summary>
/// One offer of an <see cref="EventShopPlan"/>'s cart: the slot of an event week, on one day, with the chosen
/// reward. Cost, pack size, labels and caps are not stored; the client derives them from the catalog.
/// </summary>
public sealed class EventShopCartEntry
{
    /// <summary>1-based event week.</summary>
    public int Week { get; set; }

    /// <summary><c>MON</c>..<c>SUN</c>.</summary>
    public required string Day { get; set; }

    /// <summary>0-based slot within that week's slots.</summary>
    public int SlotIndex { get; set; }

    /// <summary>The reward type of the chosen variant (e.g. <c>shards_astraYarrick</c>).</summary>
    public required string RewardType { get; set; }

    /// <summary>Packs planned, 1..the variant's per-day cap.</summary>
    public int Quantity { get; set; }

    /// <summary>Packs already bought, 0..<see cref="Quantity"/>.</summary>
    public int PurchasedQuantity { get; set; }

    /// <summary>For a <c>draft_*</c> reward, the chosen alliance: <c>imperial</c>, <c>xenos</c> or <c>chaos</c>.</summary>
    public string? DraftAlliance { get; set; }
}
