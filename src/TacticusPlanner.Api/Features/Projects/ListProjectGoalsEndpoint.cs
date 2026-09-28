using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>Lists a project's member goals as a filtered projection of the account-wide order — the read
/// counterpart to <see cref="UpdateProjectGoalsEndpoint"/>. In-flight goals come first in global order, then
/// completed/archived ones by creation time; every member is returned regardless of status so the client
/// can tab-filter over the full list.</summary>
public sealed class ListProjectGoalsEndpoint : EndpointWithoutRequest<ListProjectGoalsResponse, GoalMapper>
{
    public override void Configure()
    {
        Get("me/projects/{projectId}/goals");
        Summary(summary =>
        {
            summary.Summary = "Lists a project's member goals in account-wide priority order.";
            summary.Response<ListProjectGoalsResponse>(StatusCodes.Status200OK, "The project's member goals, in global priority order.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No matching project owned by the caller.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var projectId = ProjectId.From(Route<Guid>("projectId"));
        var db = Resolve<PlannerDbContext>();

        // All three queries below are scoped to the caller's profile by PlannerDbContext's global query filter.
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(entity => entity.Id == projectId, ct);
        if (project is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // Revision first, so a concurrent reorder yields a stale revision rather than a stale order.
        var revision = (await db.Profiles.AsNoTracking().FirstAsync(ct)).GoalOrderRevision;
        var members = await db.ProjectGoals
            .AsNoTracking()
            .Where(entity => entity.ProjectId == projectId)
            .Join(db.Goals, pg => pg.GoalId, goal => goal.Id, (pg, goal) => goal)
            .OrderBy(goal => goal.GlobalPriority == null)
            .ThenBy(goal => goal.GlobalPriority)
            .ThenByDescending(goal => goal.CreatedAt)
            .ThenBy(goal => goal.Id)
            .ToListAsync(ct);

        var goals = members.Select(goal => new ProjectGoalSummaryResponse(Map.ToSummary(goal))).ToList();

        await Send.OkAsync(new ListProjectGoalsResponse(goals, revision), ct);
    }
}

public sealed record ListProjectGoalsResponse(List<ProjectGoalSummaryResponse> Goals, long OrderRevision = 0);

public sealed record ProjectGoalSummaryResponse(GoalSummaryResponse Goal);
