namespace TacticusPlanner.Domain.PlayerData.Chunks;

/// <summary>One Legendary Release Event's player progress. Static event structure (battle configs,
/// objectives, enemies) already lives in the game catalog's <c>lres</c>/<c>lre-battles</c> datasets and is
/// intentionally not duplicated here. Track shape mirrors <c>GameCatalogLreView</c>'s named
/// Alpha/Beta/Gamma properties (Tacticus's lane ids 1/2/3) rather than a generic indexed list.</summary>
public sealed class LreProgressRecord
{
    /// <summary>The event's unit snowprint id (e.g. <c>"emperLucius"</c>) — matches the catalog's
    /// <c>GameCatalogLreView.Id</c> directly; no id remapping needed for LRE.</summary>
    public UnitId Id { get; set; } = UnitId.From(string.Empty);

    public LreTrackProgressRecord? Alpha { get; set; }

    public LreTrackProgressRecord? Beta { get; set; }

    public LreTrackProgressRecord? Gamma { get; set; }

    public int CurrentPoints { get; set; }

    public int CurrentCurrency { get; set; }

    public int CurrentShards { get; set; }

    public int CurrentClaimedChestIndex { get; set; }

    public int? CurrentEventRun { get; set; }

    public TokenBucketRecord? CurrentEventTokens { get; set; }

    public bool? HasUsedAdForExtraTokenToday { get; set; }

    public int? ExtraCurrencyPerPayout { get; set; }
}

public sealed class LreTrackProgressRecord
{
    public List<LreEncounterProgressRecord> Encounters { get; set; } = [];
}

public sealed class LreEncounterProgressRecord
{
    public List<int> ObjectivesCleared { get; set; } = [];

    public int HighScore { get; set; }

    public int EncounterPoints { get; set; }
}
