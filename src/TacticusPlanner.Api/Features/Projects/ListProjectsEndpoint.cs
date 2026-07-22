using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>Lists the authenticated user's projects, provisioning the default project ("My Goals") on
/// first access — every profile always has at least one project (plan §5).</summary>
public sealed class ListProjectsEndpoint : EndpointWithoutRequest<ListProjectsResponse, ProjectMapper>
{
    public override void Configure()
    {
        Get("me/projects");
        Summary(summary =>
        {
            summary.Summary = "Lists the authenticated user's projects.";
            summary.Description = "Creates the default project ('My Goals') on first access, so this "
                + "always returns at least one project.";
            summary.Response<ListProjectsResponse>(StatusCodes.Status200OK, "The caller's projects.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated account/profile has not been provisioned.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Resolve<ProjectsService>().EnsureDefaultProjectAsync(profileId, ct);

        var db = Resolve<PlannerDbContext>();
        var profile = await db.Profiles.AsNoTracking().FirstAsync(entity => entity.Id == profileId, ct);

        // Scoped to the caller's profile by PlannerDbContext's global query filter.
        var projects = await db.Projects
            .AsNoTracking()
            .OrderByDescending(entity => entity.IsDefault)
            .ThenBy(entity => entity.CreatedAt)
            .ToListAsync(ct);

        await Send.OkAsync(
            new ListProjectsResponse(projects.Select(project => Map.ToSummary(project, profile.ActiveProjectId)).ToList()),
            ct
        );
    }
}

public sealed record ListProjectsResponse(List<ProjectSummaryResponse> Projects);
