namespace TacticusPlanner.Domain.Goals;

/// <summary>Length constants for <see cref="Goal"/> string columns — mirrors the existing
/// <c>tacticus_user_id_hash</c> max-length pattern (constants live in Domain, applied both in
/// Persistence's <c>HasMaxLength</c> and in the create/update FluentValidation rules).</summary>
public static class GoalValidation
{
    /// <summary>Generous enough for the longest real game-catalog id, with headroom.</summary>
    public const int MaxEntityIdLength = 128;

    public const int MaxNotesLength = 2000;

    /// <summary>The most goals of one <see cref="GoalType"/> a profile may hold outside Archived (Active, Paused
    /// and Completed count). Also bounds the unpaginated goal list.</summary>
    public const int MaxGoalsPerType = 200;

    /// <summary>The most entries in one goal's snapshot resource list or upgrade target list: a whole character
    /// rank ladder needs about 150 distinct upgrade materials.</summary>
    public const int MaxResourcesPerGoal = 250;

    /// <summary>The most farming-location overrides on one goal.</summary>
    public const int MaxFarmingLocations = 100;
}
