namespace TacticusPlanner.Domain.LegendaryEvents;

/// <summary>Shape limits shared by the persistence configuration and request validation.</summary>
public static class LegendaryEventPlanRules
{
    public const int MaxEventIdLength = 64;
    public const int MaxCatalogVersionLength = 64;
    public const int MaxNotesLength = 2000;
    public const int MaxTeamNameLength = 60;
    public const int MaxLaneIdLength = 8;
    public const int MaxUnitIdLength = 64;
    public const int MaxTeamSize = 5;
    public const int MinRun = 1;
    public const int MaxRun = 3;

    /// <summary>The catalog's lane keys, in served order.</summary>
    public static readonly IReadOnlyList<string> LaneIds = ["alpha", "beta", "gamma"];

    public static bool IsLane(string? laneId) =>
        laneId is not null && LaneIds.Contains(laneId, StringComparer.Ordinal);

    public static int LaneOrder(string laneId)
    {
        for (var index = 0; index < LaneIds.Count; index++)
        {
            if (string.Equals(LaneIds[index], laneId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return LaneIds.Count;
    }
}
