using FastEndpoints;
using FluentValidation;
using FluentValidation.Results;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

public sealed class CreateProjectEndpoint : Endpoint<CreateProjectRequest, ProjectSummaryResponse, ProjectMapper>
{
    public override void Configure()
    {
        Post("projects");
        Summary(summary =>
        {
            summary.Summary = "Creates a new project.";
            summary.Response<ProjectSummaryResponse>(StatusCodes.Status200OK, "The newly created project.");
            summary.Response(StatusCodes.Status400BadRequest, "A blank name or an invalid target.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated account/profile has not been provisioned.");
        });
    }

    public override async Task HandleAsync(CreateProjectRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var name = req.Name.Trim();
        if (name.Length == 0)
        {
            AddError(request => request.Name, "A project name is required.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        if (Resolve<ProjectTargets>().Validate(req.Target, ProjectType.Custom) is { } failure)
        {
            AddError(new ValidationFailure(failure.Field, failure.Message));
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();

        var project = Map.ToEntity(req);
        project.Id = ProjectId.From(Guid.CreateVersion7());
        project.ProfileId = profileId;
        project.Type = ProjectType.Custom;

        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        await Send.OkAsync(Map.ToSummary(project, []), ct);
    }
}

public sealed record CreateProjectRequest(
    string Name,
    string? Description,
    string? Color,
    ProjectTargetDto? Target = null
);

public sealed class CreateProjectValidator : Validator<CreateProjectRequest>
{
    public CreateProjectValidator()
    {
        RuleFor(request => request.Name).MaximumLength(ProjectValidation.MaxNameLength);
        RuleFor(request => request.Description).MaximumLength(ProjectValidation.MaxDescriptionLength);
        RuleFor(request => request.Color).MaximumLength(ProjectValidation.MaxColorLength);
        RuleFor(request => request.Target!.EventId)
            .MaximumLength(ProjectValidation.MaxTargetEventIdLength)
            .OverridePropertyName("target.eventId")
            .When(request => request.Target is not null);
    }
}
