using FastEndpoints;

namespace TacticusPlanner.Api.Features.EventShopPlans;

/// <summary>The one served event shop plan shape, returned by every plan endpoint. Ids only: no reward, unit or event
/// display strings, and no costs or caps (the client derives them from the catalog <c>event-shops</c> dataset).</summary>
/// <param name="EventId">The catalog <c>event-shops</c> id, e.g. <c>october-2026</c>.</param>
/// <param name="Revision">0 for a plan that is not stored.</param>
/// <param name="CatalogVersion">The catalog version the plan was last written under; null for a plan that is not stored.</param>
/// <param name="Entries">Ordered by week, then the day's position in the event's own day order (Monday-first when the
/// event is no longer in the catalog), then slot.</param>
public sealed record EventShopPlanResponse(
    string EventId,
    long Revision,
    string? CatalogVersion,
    IReadOnlyList<EventShopCartEntryResponse> Entries
);

/// <param name="Week">1-based event week.</param>
/// <param name="Day"><c>MON</c>..<c>SUN</c>.</param>
/// <param name="SlotIndex">0-based slot within that week's <c>slots</c>.</param>
/// <param name="RewardType">The reward type of the chosen variant.</param>
/// <param name="Quantity">Packs planned.</param>
/// <param name="PurchasedQuantity">Packs already bought, 0..<paramref name="Quantity"/>.</param>
/// <param name="DraftAlliance"><c>imperial</c>, <c>xenos</c> or <c>chaos</c> for a <c>draft_*</c> reward; omitted otherwise.</param>
public sealed record EventShopCartEntryResponse(
    int Week,
    string Day,
    int SlotIndex,
    string RewardType,
    int Quantity,
    int PurchasedQuantity,
    string? DraftAlliance
);

public sealed record EventShopPlanListResponse(IReadOnlyList<EventShopPlanResponse> Plans);

/// <param name="ExpectedRevision">The plan revision the caller last read (0 for a plan that does not exist).</param>
/// <param name="Entries">The whole cart; replaces the stored entries.</param>
public sealed record PutEventShopPlanRequest(long ExpectedRevision, IReadOnlyList<EventShopCartEntryRequest>? Entries);

/// <param name="DraftAlliance">Only for a <c>draft_*</c> reward; <c>imperial</c>, <c>xenos</c> or <c>chaos</c>.</param>
public sealed record EventShopCartEntryRequest(
    int Week,
    string? Day,
    int SlotIndex,
    string? RewardType,
    int Quantity,
    int PurchasedQuantity,
    string? DraftAlliance
);

public sealed class DeleteEventShopPlanRequest
{
    /// <summary>The plan revision the caller last read (0 when no plan is stored). Required.</summary>
    [QueryParam]
    public long? ExpectedRevision { get; init; }
}

public static class EventShopPlanIssueCodes
{
    public const string Stale = "eventShopPlanStale";
}

/// <summary>One field that fails validation, named as the request names it (e.g. <c>entries[2].quantity</c>).</summary>
public sealed record EventShopFieldFailure(string Field, string Message);
