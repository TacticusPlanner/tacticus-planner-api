namespace TacticusPlanner.Domain.Projects;

/// <summary>Length constants for <see cref="Project"/> string columns — mirrors the existing
/// <c>tacticus_user_id_hash</c> max-length pattern.</summary>
public static class ProjectValidation
{
    public const int MaxNameLength = 120;

    public const int MaxDescriptionLength = 2000;

    /// <summary>A CSS hex color (<c>#rrggbb</c>) plus headroom, not a name.</summary>
    public const int MaxColorLength = 16;

    /// <summary>Mirrors the API's <c>CreateGoalValidator</c>/<c>CreateCombinedGoalsValidator</c>
    /// FluentValidation rule for <c>ProjectPriorityRequest.Priority</c> — the Persistence project's check
    /// constraint on this same value sources it from here too, so the two can't drift.</summary>
    public const int MaxPriority = 10000;
}
