namespace TacticusPlanner.Domain.Goals;

/// <summary>
/// One selected shard acquisition source on a goal's <see cref="GoalConfig.AcquisitionSources"/>.
/// <see cref="Kind"/> is validated against <see cref="AcquisitionSourceKinds"/> at the API boundary;
/// <see cref="Ids"/> holds campaign battle ids for <c>Campaign</c>, shop-offer ids
/// (<c>&lt;shopId&gt;:&lt;rewardType&gt;</c>) for <c>Shop</c>, and is empty for run-based kinds
/// (<c>Onslaught</c>). Modelled as an open <c>{ kind, ids }</c> pair rather than one field per source so
/// a future <c>Incursion</c> kind (tacticus-planner-apps#106) needs no wire-contract change.
/// </summary>
public sealed class AcquisitionSource
{
    public required string Kind { get; set; }

    public List<string> Ids { get; set; } = [];
}

/// <summary>
/// The server-owned allow-list of <see cref="AcquisitionSource.Kind"/> values. Intentionally growable:
/// adding <c>Incursion</c> (a MoW-only run-based source, tacticus-planner-apps#106) is a one-line
/// addition here plus its gating rule in <c>GoalTargetValidationService</c> — no DTO or OpenAPI change.
/// </summary>
public static class AcquisitionSourceKinds
{
    public const string Campaign = "Campaign";
    public const string Onslaught = "Onslaught";
    public const string Shop = "Shop";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Campaign, Onslaught, Shop };

    /// <summary>Kinds that carry no <see cref="AcquisitionSource.Ids"/> (a non-empty list is rejected).</summary>
    public static readonly IReadOnlySet<string> RunBased =
        new HashSet<string>(StringComparer.Ordinal) { Onslaught };

    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind);
}
