using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>Lists the authenticated user's projects, provisioning the default project ("My Goals") on
/// first access — every profile always has at least one project. Each carries its member <c>goalIds</c> in global
/// order, filtered by the same archived selector as the goals list (<c>goal-collection-read</c>), so membership and
/// counts never need a per-project read.</summary>
public sealed class ListProjectsEndpoint : EndpointWithoutRequest<ListProjectsResponse, ProjectMapper>
{
    public override void Configure()
    {
        Get("me/projects");
        Summary(summary =>
        {
            summary.Summary = "Lists the authenticated user's projects.";
            summary.Description = "Creates the default project ('My Goals') on first access, so this "
                + "always returns at least one project. goalIds lists the member goals in global order: "
                + "non-archived goals by default, only archived goals with ?archived=true (the selector filters "
                + "goalIds, never the projects).";
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
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        await Resolve<ProjectsService>().EnsureDefaultProjectAsync(profileId, ct);

        var db = Resolve<PlannerDbContext>();

        // Scoped to the caller's profile by PlannerDbContext's global query filter.
        var projects = await db.Projects
            .AsNoTracking()
            .OrderByDescending(entity => entity.Type == ProjectType.Default)
            .ThenBy(entity => entity.CreatedAt)
            .ToListAsync(ct);
        var archived = Query<bool?>("archived", isRequired: false) ?? false;
        var goalIds = await db.GoalIdsByProjectAsync(archived, projectIds: null, ct);

        await Send.OkAsync(
            new ListProjectsResponse(projects
                .Select(project => Map.ToSummary(project, goalIds.GetValueOrDefault(project.Id, [])))
                .ToList()),
            ct
        );
    }
}

public sealed record ListProjectsResponse(List<ProjectSummaryResponse> Projects);
