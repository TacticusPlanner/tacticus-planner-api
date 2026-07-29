namespace TacticusPlanner.Domain.Projects;

/// <summary>Length constants for <see cref="Project"/> string columns — mirrors the existing
/// <c>tacticus_user_id_hash</c> max-length pattern.</summary>
public static class ProjectValidation
{
    public const int MaxNameLength = 120;

    public const int MaxDescriptionLength = 2000;

    /// <summary>A CSS hex color (<c>#rrggbb</c>) plus headroom, not a name.</summary>
    public const int MaxColorLength = 16;
}
