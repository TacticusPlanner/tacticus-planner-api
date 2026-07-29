using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

/// <summary>Bulk pause/resume/start every applicable goal in a project (plan §2/§5) — completed and
/// archived goals are skipped, matching V1's clarified requirement that a bulk transition never revives a
/// finished goal.</summary>
public sealed class UpdateProjectGoalsStatusEndpoint : Endpoint<UpdateProjectGoalsStatusRequest, ProjectGoalsStatusResponse>
{
    private static readonly HashSet<GoalStatus> AllowedTargets = [GoalStatus.Active, GoalStatus.Paused];

    public override void Configure()
    {
        Post("me/projects/{projectId}/goals/status");
        Summary(summary =>
        {
            summary.Summary = "Bulk pauses or resumes every applicable goal in a project.";
            summary.Description = "Completed and archived member goals are left untouched.";
            summary.Response<ProjectGoalsStatusResponse>(StatusCodes.Status200OK, "How many goals were transitioned.");
            summary.Response(StatusCodes.Status400BadRequest, "Unknown or unsupported target status.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No matching project owned by the caller.");
        });
    }

    public override async Task HandleAsync(UpdateProjectGoalsStatusRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!Enum.TryParse<GoalStatus>(req.Status, ignoreCase: true, out var targetStatus)
            || !AllowedTargets.Contains(targetStatus))
        {
            AddError(request => request.Status, "Unknown or unsupported target status.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var projectId = ProjectId.From(Route<Guid>("projectId"));
        var db = Resolve<PlannerDbContext>();

        // Scoped to the caller's profile by PlannerDbContext's global query filter.
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(entity => entity.Id == projectId, ct);
        if (project is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var memberGoalIds = await db.ProjectGoals
            .Where(entity => entity.ProjectId == projectId)
            .Select(entity => entity.GoalId)
            .ToListAsync(ct);

        var goals = await db.Goals
            .Where(entity => memberGoalIds.Contains(entity.Id)
                && entity.Status != GoalStatus.Completed
                && entity.Status != GoalStatus.Archived)
            .ToListAsync(ct);

        var eventType = targetStatus == GoalStatus.Active ? GoalEventType.Resumed : GoalEventType.Paused;
        var transitioned = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var goal in goals)
        {
            if (goal.Status == targetStatus)
            {
                continue;
            }

            goal.Status = targetStatus;
            goal.Events.Add(new GoalEvent { At = now, Type = eventType });
            transitioned++;
        }

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new ProjectGoalsStatusResponse(transitioned), ct);
    }
}

public sealed record UpdateProjectGoalsStatusRequest(string Status);

public sealed record ProjectGoalsStatusResponse(int GoalsTransitioned);
