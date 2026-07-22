using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>Marks a project as the profile's single active plan (plan §3.2/§5) by pointing
/// <see cref="Profiles.Profile.ActiveProjectId"/> at it — a single scalar update, so "at most one active
/// plan per profile" is structural (there's only one pointer) rather than enforced by a per-project flag
/// plus a partial unique index and a clear-then-set two-step save.</summary>
public sealed class ActivateProjectEndpoint : EndpointWithoutRequest<ProjectSummaryResponse, ProjectMapper>
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

        // Scoped to the caller's profile by PlannerDbContext's global query filter.
        var project = await db.Projects.FirstOrDefaultAsync(entity => entity.Id == projectId, ct);
        if (project is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var profile = await db.Profiles.FirstAsync(entity => entity.Id == profileId, ct);
        profile.ActiveProjectId = projectId;

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(Map.ToSummary(project, projectId), ct);
    }
}
