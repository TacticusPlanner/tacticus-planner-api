using TacticusPlanner.Domain.Projects;

namespace TacticusPlanner.Api.Features.Projects;

public static class ProjectProjection
{
    public static ProjectSummaryResponse BuildSummary(Project project) => new(
        project.Id.Value,
        project.Name,
        project.Description,
        project.Color,
        project.Status.ToString(),
        project.IsActivePlan,
        project.IsDefault,
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
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
