using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.RosterSnapshots;

namespace TacticusPlanner.Domain.LegendaryEvents;

/// <summary>
/// One run occurrence of a Legendary Event as the profile played it (LRE Stage 7, design D1): what was cleared,
/// with which plan teams, and the roster as it was (inline). Written only by sync (opened or refreshed,
/// design D2), never through the API; keyed by the catalog event id with no FK, so it outlives the catalog entry.
/// </summary>
public class LegendaryEventRunRecord : BaseEntity<LegendaryEventRunRecordId>
{
    public ProfileId ProfileId { get; set; }

    /// <summary>The catalog <c>lres</c> id at capture time.</summary>
    public required string EventId { get; set; }

    /// <summary>The event run, 1–3.</summary>
    public int Run { get; set; }

    /// <summary>The first sync that saw this occurrence.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>The last sync that changed its lanes.</summary>
    public DateTimeOffset ProgressAt { get; set; }

    /// <summary>The catalog version at <see cref="ProgressAt"/>.</summary>
    public required string CatalogVersion { get; set; }

    /// <summary>The synced event points at <see cref="ProgressAt"/>.</summary>
    public int Points { get; set; }

    public LegendaryEventRunRecordLanes Lanes { get; set; } = new();

    /// <summary>The plan's teams at <see cref="ProgressAt"/>, in lane then sort order (copies of the plan's teams;
    /// the served depth is each team's depth for <see cref="Run"/>).</summary>
    public List<LegendaryEventTeamRecord> Teams { get; set; } = [];

    /// <summary>This record's own roster snapshot, never null; read on demand, never with the record list.</summary>
    public RosterSnapshot Roster { get; set; } = new();

    public virtual Profile? Profile { get; set; }
}

/// <summary>Each lane is null as synced, or its encounters in order.</summary>
public sealed class LegendaryEventRunRecordLanes
{
    public LegendaryEventRunRecordLane? Alpha { get; set; }

    public LegendaryEventRunRecordLane? Beta { get; set; }

    public LegendaryEventRunRecordLane? Gamma { get; set; }
}

public sealed class LegendaryEventRunRecordLane
{
    public List<LegendaryEventRunRecordEncounter> Encounters { get; set; } = [];
}

/// <summary>A synced encounter without <c>encounterPoints</c>, which the client never uses.</summary>
public sealed class LegendaryEventRunRecordEncounter
{
    public List<int> ObjectivesCleared { get; set; } = [];

    public int HighScore { get; set; }
}

