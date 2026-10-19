using TacticusPlanner.Domain.Projects;
using TacticusPlanner.GameCatalog;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>A project's optional target on the wire: <c>{ kind, eventId }</c>, where <c>kind</c> is
/// <c>legendaryEvent</c> and <c>eventId</c> a served <c>lres</c> id (spec: project-event-target).</summary>
public sealed record ProjectTargetDto(string Kind, string EventId);

/// <summary>
/// Validates and applies project targets against the current Game Catalog. Events are catalog records, not
/// rows, so the id is checked at write time only; a target whose event a later catalog drops still reads.
/// </summary>
public sealed class ProjectTargets(IGameCatalogProvider catalog)
{
    public const string LegendaryEventKind = "legendaryEvent";

    /// <summary>The field and message of the first problem with <paramref name="target"/>, or null when it can be
    /// stored on a project of <paramref name="type"/>. A null target is always valid, and so is resending the
    /// <paramref name="stored"/> one: renaming or archiving a project whose event the catalog dropped still saves.</summary>
    public (string Field, string Message)? Validate(
        ProjectTargetDto? target, ProjectType type, ProjectTargetDto? stored = null)
    {
        if (target is null || target == stored)
        {
            return null;
        }

        if (type == ProjectType.Default)
        {
            return ("target", "The default project cannot have a target.");
        }

        if (target.Kind != LegendaryEventKind)
        {
            return ("target.kind", $"target.kind must be {LegendaryEventKind}.");
        }

        return catalog.Current.LreViews.Any(view => view.Id == target.EventId)
            ? null
            : ("target.eventId", "target.eventId must be a Legendary Event id.");
    }

    public static void Apply(Project project, ProjectTargetDto? target)
    {
        project.TargetKind = target is null ? null : ProjectTargetKind.LegendaryEvent;
        project.TargetEventId = target?.EventId;
    }

    public static ProjectTargetDto? ToDto(Project project) =>
        project is { TargetKind: ProjectTargetKind.LegendaryEvent, TargetEventId: { } eventId }
            ? new ProjectTargetDto(LegendaryEventKind, eventId)
            : null;
}
