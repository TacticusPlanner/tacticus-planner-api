using FastEndpoints;
using TacticusPlanner.Domain.Projects;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>
/// Maps between <see cref="Project"/> and its API request/response shapes. <c>IsActivePlan</c> on the
/// response is computed (the entity itself no longer carries that flag — see
/// <see cref="Profiles.Profile.ActiveProjectId"/>), so mapping needs the caller's active project id
/// alongside the entity; <see cref="ToSummary"/> takes it as a parameter rather than overriding
/// <c>FromEntity</c>, whose signature only accepts the entity.
/// </summary>
public sealed class ProjectMapper : Mapper<CreateProjectRequest, ProjectSummaryResponse, Project>
{
    public override Project ToEntity(CreateProjectRequest r) => new()
    {
        Name = r.Name.Trim(),
        Description = r.Description,
        Color = r.Color,
        Status = ProjectStatus.Active,
    };

    public ProjectSummaryResponse ToSummary(Project project, ProjectId? activeProjectId) => new(
        project.Id.Value,
        project.Name,
        project.Description,
        project.Color,
        project.Status.ToString(),
        project.Id == activeProjectId,
        project.IsDefault,
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
    bool IsActivePlan,
    bool IsDefault,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
