using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

public sealed class CreateProjectEndpoint : Endpoint<CreateProjectRequest, ProjectSummaryResponse, ProjectMapper>
{
    public override void Configure()
    {
        Post("me/projects");
        Summary(summary =>
        {
            summary.Summary = "Creates a new project.";
            summary.Response<ProjectSummaryResponse>(StatusCodes.Status200OK, "The newly created project.");
            summary.Response(StatusCodes.Status400BadRequest, "A blank name.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated account/profile has not been provisioned.");
        });
    }

    public override async Task HandleAsync(CreateProjectRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var name = req.Name.Trim();
        if (name.Length == 0)
        {
            AddError(request => request.Name, "A project name is required.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var profile = await db.Profiles.AsNoTracking().FirstAsync(entity => entity.Id == profileId, ct);

        var project = Map.ToEntity(req);
        project.Id = ProjectId.From(Guid.CreateVersion7());
        project.ProfileId = profileId;
        project.Type = ProjectType.Custom;

        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        await Send.OkAsync(Map.ToSummary(project, profile.ActiveProjectId), ct);
    }
}

public sealed record CreateProjectRequest(string Name, string? Description, string? Color);

public sealed class CreateProjectValidator : Validator<CreateProjectRequest>
{
    public CreateProjectValidator()
    {
        RuleFor(request => request.Name).MaximumLength(ProjectValidation.MaxNameLength);
        RuleFor(request => request.Description).MaximumLength(ProjectValidation.MaxDescriptionLength);
        RuleFor(request => request.Color).MaximumLength(ProjectValidation.MaxColorLength);
    }
}
