using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

public sealed class UpdateProjectEndpoint : Endpoint<UpdateProjectRequest, ProjectSummaryResponse, ProjectMapper>
{
    public override void Configure()
    {
        Put("me/projects/{projectId}");
        Summary(summary =>
        {
            summary.Summary = "Updates or archives a project.";
            summary.Response<ProjectSummaryResponse>(StatusCodes.Status200OK);
            summary.Response<ProjectConflictResponse>(StatusCodes.Status409Conflict);
        });
    }

    public override async Task HandleAsync(UpdateProjectRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var projectId = ProjectId.From(Route<Guid>("projectId"));
        // Scoped to the caller's profile by PlannerDbContext's global query filter.
        var project = await db.Projects
            .FirstOrDefaultAsync(entity => entity.Id == projectId, ct);
        if (project is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (project.Revision != req.Revision)
        {
            await SendConflictAsync("staleRevision", "The project changed on another device. Refresh and try again.", ct);
            return;
        }

        var status = Enum.Parse<ProjectStatus>(req.Status, ignoreCase: true);
        var profile = await db.Profiles.FirstAsync(entity => entity.Id == profileId, ct);
        if (status == ProjectStatus.Archived && project.Type == ProjectType.Default)
        {
            await SendConflictAsync("defaultProjectCannotBeArchived", "The default project cannot be archived.", ct);
            return;
        }

        if (status == ProjectStatus.Archived && profile.ActiveProjectId == project.Id)
        {
            await SendConflictAsync("activeProjectCannotBeArchived", "Activate another project before archiving this one.", ct);
            return;
        }

        project.Name = req.Name.Trim();
        project.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        project.Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim();
        project.Status = status;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await SendConflictAsync("staleRevision", "The project changed on another device. Refresh and try again.", ct);
            return;
        }

        await Send.OkAsync(Map.ToSummary(project, profile.ActiveProjectId), ct);
    }

    private async Task SendConflictAsync(string issueCode, string message, CancellationToken ct)
    {
        HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        await HttpContext.Response.WriteAsJsonAsync(new ProjectConflictResponse(issueCode, message), ct);
    }
}

public sealed record UpdateProjectRequest(
    string Name,
    string? Description,
    string? Color,
    string Status,
    long Revision
);

public sealed record ProjectConflictResponse(string IssueCode, string Message);

public sealed class UpdateProjectValidator : Validator<UpdateProjectRequest>
{
    public UpdateProjectValidator()
    {
        RuleFor(request => request.Name).NotEmpty();
        RuleFor(request => request.Status)
            .Must(value => Enum.TryParse<ProjectStatus>(value, ignoreCase: true, out var status)
                && Enum.IsDefined(status))
            .WithMessage("Status must be Active, Paused, or Archived.");
        RuleFor(request => request.Revision).GreaterThanOrEqualTo(0);
    }
}
