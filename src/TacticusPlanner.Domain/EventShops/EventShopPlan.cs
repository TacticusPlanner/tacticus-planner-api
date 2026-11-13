using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Domain.EventShops;

/// <summary>
/// A profile's plan for one seasonal event shop: the cart of offers it means to buy, per event day, with how many
/// it already bought. Read and written whole as one row (the entries are jsonb). One plan per (profile, catalog
/// <c>event-shops</c> id). <see cref="Revision"/> guards every mutation; the shape invariants the database does not
/// see are asserted by <see cref="EventShopValidation.AssertInvariants"/> before save.
/// </summary>
public class EventShopPlan : BaseEntity<EventShopPlanId>, IRevisionedEntity
{
    public long Revision { get; set; }

    public ProfileId ProfileId { get; set; }

    /// <summary>The catalog <c>event-shops</c> id (e.g. <c>october-2026</c>).</summary>
    public required string EventId { get; set; }

    /// <summary>The catalog version the plan was last written under.</summary>
    public required string CatalogVersion { get; set; }

    public virtual Profile? Profile { get; set; }

    public List<EventShopCartEntry> Entries { get; set; } = [];
}
