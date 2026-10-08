using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Domain.LegendaryEvents;

/// <summary>
/// A profile's plan for one Legendary Event (LRE Stage 2): its lane teams and plan-level fields. One plan per
/// (profile, catalog event id). <see cref="Revision"/> guards every mutation of the plan or its teams, so a
/// team write always touches the plan row (see <c>LegendaryEventPlanWriter</c>).
/// </summary>
public class LegendaryEventPlan : BaseEntity<LegendaryEventPlanId>, IRevisionedEntity
{
    public long Revision { get; set; }

    public ProfileId ProfileId { get; set; }

    /// <summary>The catalog <c>lres</c> id (the event unit's snowprint id, e.g. <c>astarLysander</c>).</summary>
    public required string EventId { get; set; }

    /// <summary>The catalog version the plan was last written under (<c>GameCatalogRelease.Version</c>).</summary>
    public required string CatalogVersion { get; set; }

    public string? Notes { get; set; }

    public bool ShowPaidOptions { get; set; }

    public virtual Profile? Profile { get; set; }

    public virtual ICollection<LegendaryEventTeam> Teams { get; set; } = new List<LegendaryEventTeam>();
}
