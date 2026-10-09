using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Domain.LegendaryEvents;

/// <summary>
/// A profile's plan for one catalog Legendary Event: plan-level fields plus its lane teams (LRE plan
/// Stage 2, ADR 0008). One row per profile and event. Every mutation of the plan or anything beneath it
/// bumps <see cref="Revision"/>, the single concurrency token the client echoes as <c>expectedRevision</c>.
/// </summary>
public class LegendaryEventPlan : BaseEntity<LegendaryEventPlanId>, IRevisionedEntity
{
    public long Revision { get; set; }

    public ProfileId ProfileId { get; set; }

    /// <summary>The catalog <c>lres</c> id (the event's unit snowprint id, e.g. <c>astarLysander</c>).</summary>
    public required string EventId { get; set; }

    /// <summary>The <c>GameCatalogRelease.Version</c> the plan was last written under. Echoed so the
    /// client can warn about drift; never used to reject a read.</summary>
    public string CatalogVersion { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public bool ShowPaidOptions { get; set; }

    public virtual List<LegendaryEventTeam> Teams { get; set; } = [];

    public virtual Profile? Profile { get; set; }
}
