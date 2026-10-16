using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.Domain.Profiles;
using Vogen;

namespace TacticusPlanner.Domain.RosterSnapshots;

[ValueObject<Guid>]
public readonly partial struct RosterSnapshotId : IGuidValueObject;

/// <summary>
/// The profile's whole owned roster as it was at <see cref="TakenAt"/> (LRE Stage 7, design D4): every character
/// and MoW in the synced <c>characters</c> / <c>mows</c> chunk shapes, so a later diff compares like with like.
/// The foundation for the later port of V1's roster snapshots feature; for now only
/// <see cref="RosterSnapshotSource.LegendaryEventRun"/> rows exist, each owned by one
/// <c>LegendaryEventRunRecord</c> and rewritten in place when that record refreshes.
/// </summary>
public class RosterSnapshot : BaseEntity<RosterSnapshotId>
{
    public ProfileId ProfileId { get; set; }

    public RosterSnapshotSource Source { get; set; }

    /// <summary>Null for <see cref="RosterSnapshotSource.LegendaryEventRun"/>; the user's name for a later manual
    /// snapshot.</summary>
    public string? Name { get; set; }

    /// <summary>When the units were read.</summary>
    public DateTimeOffset TakenAt { get; set; }

    /// <summary>The catalog version at <see cref="TakenAt"/>.</summary>
    public required string CatalogVersion { get; set; }

    public List<PlayerCharacterRecord> Characters { get; set; } = [];

    public List<PlayerMowRecord> Mows { get; set; } = [];

    public virtual Profile? Profile { get; set; }
}

public enum RosterSnapshotSource
{
    LegendaryEventRun,
}

public static class RosterSnapshotValidation
{
    public const int MaxNameLength = 60;

    /// <summary>The stored and served name of <see cref="RosterSnapshotSource.LegendaryEventRun"/>.</summary>
    public const string LegendaryEventRunSource = "legendaryEventRun";
}
