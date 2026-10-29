using FastEndpoints;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
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
            summary.Response(StatusCodes.Status400BadRequest, "A blank name, an invalid target, or projectLimitReached: the profile already has 100 projects.");
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

        // Counted under the profile lock so concurrent creates cannot both take the last slot.
        await Resolve<ProjectGoalPlanningService>().ExecuteLockedMutationAsync(async transaction =>
        {
            if (await db.Projects.CountAsync(p => p.Status != ProjectStatus.Archived, ct) >= ProjectValidation.MaxProjects)
            {
                AddError(ProjectLimitFailure());
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
            }

            db.Projects.Add(project);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await Send.OkAsync(Map.ToSummary(project, []), ct);
        }, ct);
    }

    public const string ProjectLimitReached = "projectLimitReached";

    /// <summary>400 <c>projectLimitReached</c>: the profile already holds <see cref="ProjectValidation.MaxProjects"/>
    /// non-archived projects.</summary>
    internal static ValidationFailure ProjectLimitFailure() =>
        new("general", $"A profile can have at most {ProjectValidation.MaxProjects} non-archived projects; archive one to make room.")
        {
            ErrorCode = ProjectLimitReached,
        };
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
