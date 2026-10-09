namespace TacticusPlanner.Domain.LegendaryEvents;

/// <summary>Shape limits for Legendary Event plans and teams (design D6, D7, D12).</summary>
public static class LegendaryEventValidation
{
    public static readonly IReadOnlyList<string> LaneIds = ["alpha", "beta", "gamma"];

    public const int MaxEventIdLength = 128;

    public const int MaxCatalogVersionLength = 64;

    public const int MaxNotesLength = 2000;

    public const int MaxTeamNameLength = 60;

    public const int MaxUnitIdLength = 128;

    public const int MaxMembers = 5;

    public const int MinRun = 1;

    public const int MaxRun = 3;
}
