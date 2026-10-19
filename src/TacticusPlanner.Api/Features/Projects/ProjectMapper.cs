using FastEndpoints;
using TacticusPlanner.Domain.Projects;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>
/// Maps between <see cref="Project"/> and its API request/response shapes.
/// </summary>
public sealed class ProjectMapper : Mapper<CreateProjectRequest, ProjectSummaryResponse, Project>
{
    public override Project ToEntity(CreateProjectRequest r) => new()
    {
        Name = r.Name.Trim(),
        Description = r.Description,
        Color = r.Color,
        Status = ProjectStatus.Active,
        TargetKind = r.Target is null ? null : ProjectTargetKind.LegendaryEvent,
        TargetEventId = r.Target?.EventId,
    };

    public ProjectSummaryResponse ToSummary(Project project) => new(
        project.Id.Value,
        project.Name,
        project.Description,
        project.Color,
        project.Status.ToString(),
        project.Type == ProjectType.Default,
        ProjectTargets.ToDto(project),
        project.Revision,
        project.CreatedAt,
        project.UpdatedAt
    );
}

public sealed record ProjectSummaryResponse(
    Guid ProjectId,
    string Name,
    string? Description,
    string? Color,
    string Status,
    bool IsDefault,
    ProjectTargetDto? Target,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
