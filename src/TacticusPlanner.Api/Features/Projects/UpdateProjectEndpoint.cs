using FastEndpoints;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
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
            summary.Description = "An absent or null target clears it; archiving keeps it.";
            summary.Response<ProjectSummaryResponse>(StatusCodes.Status200OK);
            summary.Response(StatusCodes.Status409Conflict,
                "projectRevisionStale or defaultProjectCannotBeArchived; current is the project as it stands.");
        });
    }

    public override async Task HandleAsync(UpdateProjectRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var projectId = ProjectId.From(Route<Guid>("projectId"));
        // Scoped to the caller's profile by PlannerDbContext's global query filter.
        var project = await db.Projects
            .FirstOrDefaultAsync(entity => entity.Id == projectId, ct);
        if (project is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        if (project.Revision != req.Revision)
        {
            await SendStaleAsync(db, project, ct);
            return;
        }

        var status = Enum.Parse<ProjectStatus>(req.Status, ignoreCase: true);
        if (status == ProjectStatus.Archived && project.Type == ProjectType.Default)
        {
            await this.SendConflictAsync(
                "defaultProjectCannotBeArchived", "The default project cannot be archived.",
                Map.ToSummary(project, await db.GoalIdsAsync(project.Id, ct)), ct);
            return;
        }

        if (Resolve<ProjectTargets>().Validate(req.Target, project.Type, ProjectTargets.ToDto(project)) is { } failure)
        {
            AddError(new ValidationFailure(failure.Field, failure.Message));
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        project.Name = req.Name.Trim();
        project.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        project.Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim();
        project.Status = status;
        ProjectTargets.Apply(project, req.Target);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var current = await db.Projects.AsNoTracking().FirstOrDefaultAsync(entity => entity.Id == projectId, ct);
            if (current is null)
                await this.SendNotFoundProblemAsync(ct);
            else
                await SendStaleAsync(db, current, ct);
            return;
        }

        await Send.OkAsync(Map.ToSummary(project, await db.GoalIdsAsync(project.Id, ct)), ct);
    }

    /// <summary>409 <c>projectRevisionStale</c> with the project in its list shape under <c>current</c>.</summary>
    private async Task SendStaleAsync(PlannerDbContext db, Project current, CancellationToken ct) =>
        await this.SendConflictAsync(
            "projectRevisionStale", "The project changed on another device. Refresh and try again.",
            Map.ToSummary(current, await db.GoalIdsAsync(current.Id, ct)), ct);
}

public sealed record UpdateProjectRequest(
    string Name,
    string? Description,
    string? Color,
    string Status,
    long Revision,
    ProjectTargetDto? Target = null
);

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
        RuleFor(request => request.Target!.EventId)
            .MaximumLength(ProjectValidation.MaxTargetEventIdLength)
            .OverridePropertyName("target.eventId")
            .When(request => request.Target is not null);
    }
}
