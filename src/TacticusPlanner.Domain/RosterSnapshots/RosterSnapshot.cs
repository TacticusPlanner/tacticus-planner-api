using TacticusPlanner.Domain.PlayerData.Chunks;

namespace TacticusPlanner.Domain.RosterSnapshots;

/// <summary>
/// The profile's whole owned roster as it was at <see cref="TakenAt"/> (LRE Stage 7, design D4): every character
/// and MoW in the synced <c>characters</c> / <c>mows</c> chunk shapes, so a later diff compares like with like.
/// Has no identity of its own: it is stored inline (jsonb) on the owner that took it (today a
/// <c>LegendaryEventRunRecord</c>) and rewritten in place when that owner refreshes.
/// </summary>
public sealed class RosterSnapshot
{
    /// <summary>When the units were read.</summary>
    public DateTimeOffset TakenAt { get; set; }

    /// <summary>The catalog version at <see cref="TakenAt"/>.</summary>
    public string CatalogVersion { get; set; } = string.Empty;

    public List<PlayerCharacterRecord> Characters { get; set; } = [];

    public List<PlayerMowRecord> Mows { get; set; } = [];
}
