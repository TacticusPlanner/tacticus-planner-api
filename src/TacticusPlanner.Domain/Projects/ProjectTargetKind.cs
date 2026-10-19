namespace TacticusPlanner.Domain.Projects;

/// <summary>What a project's optional target points at. Persisted as a string; open so other event types can
/// follow.</summary>
public enum ProjectTargetKind
{
    /// <summary><see cref="Project.TargetEventId"/> is a Game Catalog <c>lres</c> id.</summary>
    LegendaryEvent = 1,
}
