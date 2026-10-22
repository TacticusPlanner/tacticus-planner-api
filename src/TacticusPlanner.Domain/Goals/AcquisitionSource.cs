namespace TacticusPlanner.Domain.Goals;

/// <summary>
/// One selected acquisition source on a goal's <see cref="GoalConfig.AcquisitionSources"/> — a shard
/// source on Unlock/Ascension goals, or a Mythic-material shop offer on Rank/Upgrade/MoW-Ability goals.
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
/// The server-owned allow-list of <see cref="AcquisitionSource.Kind"/> values (stored by name). Intentionally
/// growable: adding <c>Incursion</c> (a MoW-only run-based source, tacticus-planner-apps#106) is a member
/// here plus its gating rule in <c>AcquisitionSourceRules</c>; the OpenAPI document picks the member up.
/// </summary>
public enum AcquisitionSourceKind
{
    Campaign,
    Onslaught,
    Shop,
}

public static class AcquisitionSourceKinds
{
    /// <summary>Kinds that carry no <see cref="AcquisitionSource.Ids"/> (a non-empty list is rejected).</summary>
    public static readonly IReadOnlySet<AcquisitionSourceKind> RunBased =
        new HashSet<AcquisitionSourceKind> { AcquisitionSourceKind.Onslaught };
}

/// <summary>
/// The four uncraftable Mythic upgrade materials (Imperial Aquila, Mutant Form, Ancient Inscription,
/// Venerable Battle Mark). Their reliable source is daily shops, so a Rank/Upgrade/MoW-Ability goal may
/// carry <c>Shop</c> acquisition sources for them (<c>&lt;shopId&gt;:upgHpM00x</c>). The client keeps the
/// same list (<c>MYTHIC_UNCRAFTABLE_UPGRADES</c>).
/// </summary>
public static class MythicMaterialIds
{
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { "upgHpM001", "upgHpM002", "upgHpM003", "upgHpM004" };
}
