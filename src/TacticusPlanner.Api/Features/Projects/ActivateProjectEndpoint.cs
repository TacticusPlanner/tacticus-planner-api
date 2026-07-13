using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>Marks a project as the profile's single active plan (plan §3.2/§5), clearing the flag on any
/// other project first — the partial unique index on <c>projects(profile_id) WHERE is_active_plan</c>
/// would otherwise reject setting two projects active at once.</summary>
public sealed class ActivateProjectEndpoint : EndpointWithoutRequest<ProjectSummaryResponse>
{
    public override void Configure()
    {
        Post("me/projects/{projectId}/activate");
        Summary(summary =>
        {
            summary.Summary = "Sets a project as the profile's active plan.";
            summary.Response<ProjectSummaryResponse>(StatusCodes.Status200OK, "The now-active project.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No matching project owned by the caller.");
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

        var projectId = ProjectId.From(Route<Guid>("projectId"));
        var db = Resolve<PlannerDbContext>();

        var project = await db.Projects.FirstOrDefaultAsync(
            entity => entity.Id == projectId && entity.ProfileId == profileId,
            ct
        );
        if (project is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!project.IsActivePlan)
        {
            var currentlyActive = await db.Projects
                .Where(entity => entity.ProfileId == profileId && entity.IsActivePlan && entity.Id != projectId)
                .ToListAsync(ct);

            foreach (var other in currentlyActive)
            {
                other.IsActivePlan = false;
            }

            if (currentlyActive.Count > 0)
            {
                await db.SaveChangesAsync(ct);
            }

            project.IsActivePlan = true;
            await db.SaveChangesAsync(ct);
        }

        await Send.OkAsync(ProjectProjection.BuildSummary(project), ct);
    }
}
