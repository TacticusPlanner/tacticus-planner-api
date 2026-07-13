using FastEndpoints;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

public sealed class CreateProjectEndpoint : Endpoint<CreateProjectRequest, ProjectSummaryResponse>
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

        var project = new Project
        {
            Id = ProjectId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            Name = name,
            Description = req.Description,
            Color = req.Color,
            Status = ProjectStatus.Active,
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        await Send.OkAsync(ProjectProjection.BuildSummary(project), ct);
    }
}

public sealed record CreateProjectRequest(string Name, string? Description, string? Color);
