namespace TacticusPlanner.Domain.Goals;

/// <summary>Length constants for <see cref="Goal"/> string columns — mirrors the existing
/// <c>tacticus_user_id_hash</c> max-length pattern (constants live in Domain, applied both in
/// Persistence's <c>HasMaxLength</c> and in the create/update FluentValidation rules).</summary>
public static class GoalValidation
{
    /// <summary>Generous enough for the longest real game-catalog id, with headroom.</summary>
    public const int MaxEntityIdLength = 128;

    public const int MaxNotesLength = 2000;
}
