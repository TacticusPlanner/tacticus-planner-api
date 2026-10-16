namespace TacticusPlanner.Domain.LegendaryEvents;

/// <summary>Shape limits for Legendary Event plans and teams (Stage 2 design D6, D7, D12; Stage 3 run inputs and annotations).</summary>
public static class LegendaryEventValidation
{
    public static readonly IReadOnlyList<string> LaneIds = ["alpha", "beta", "gamma"];

    public const int MaxEventIdLength = 128;

    public const int MaxCatalogVersionLength = 64;

    public const int MaxNotesLength = 2000;

    public const int MaxTeamNameLength = 60;

    public const int MaxUnitIdLength = 128;

    /// <summary>The fewest non-reserve members a team can field in the game; the reserve does not count.</summary>
    public const int MinMembers = 3;

    public const int MaxMembers = 5;

    public const int MinRun = 1;

    public const int MaxRun = 3;

    /// <summary>V1's bound for "Oh So Close" shards per run: three chest-sized awards.</summary>
    public const int MaxCloseShards = 75;

    /// <summary>Objective ids of a battle cell: 0 is defeat-all, 1..5 the lane objectives.</summary>
    public const int MaxObjectiveId = 5;

    public const string MaybeClearStatus = "maybeClear";

    public const string StopHereStatus = "stopHere";
}
