using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>Lists a project's member goals with their per-project priority ordering (plan §5/§8) — the
/// read counterpart to <see cref="UpdateProjectGoalsEndpoint"/>. Returns every member goal regardless of
/// status (including archived) so the client can tab-filter and reorder over the full ordering itself.</summary>
public sealed class ListProjectGoalsEndpoint : EndpointWithoutRequest<ListProjectGoalsResponse, GoalMapper>
{
    public override void Configure()
    {
        Get("me/projects/{projectId}/goals");
        Summary(summary =>
        {
            summary.Summary = "Lists a project's member goals with their per-project priority.";
            summary.Response<ListProjectGoalsResponse>(StatusCodes.Status200OK, "The project's member goals, ordered by priority.");
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

        var project = await db.Projects.Owned(profileId).AsNoTracking().FirstOrDefaultAsync(entity => entity.Id == projectId, ct);
        if (project is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var members = await db.ProjectGoals
            .AsNoTracking()
            .Where(entity => entity.ProjectId == projectId)
            .Join(db.Goals.Owned(profileId), pg => pg.GoalId, goal => goal.Id, (pg, goal) => new { pg.Priority, Goal = goal })
            .OrderBy(entity => entity.Priority)
            .ToListAsync(ct);

        var goals = members.Select(entity => new ProjectGoalSummaryResponse(Map.ToSummary(entity.Goal), entity.Priority)).ToList();

        await Send.OkAsync(new ListProjectGoalsResponse(goals), ct);
    }
}

public sealed record ListProjectGoalsResponse(List<ProjectGoalSummaryResponse> Goals);

public sealed record ProjectGoalSummaryResponse(GoalSummaryResponse Goal, int Priority);
