using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Creates several goals for one entity in a single call, linked together — the combined-creation flow
/// (plan §6/§8): pick a character, compose any combination of Unlock/Ascension/Rank/Ability targets, the
/// client detects unmet prerequisites (a locked entity needs Unlock; an unreachable target Rank needs
/// Ascension) and orders the request accordingly, then this endpoint persists the whole set atomically —
/// one shared <see cref="Goal.AggregateId"/>, and each spec's <see cref="CombinedGoalSpec.DependsOnIndex"/>
/// resolved into real <see cref="Goal.DependsOn"/> edges. Detection/ordering itself stays client-side (it
/// needs live player-data the server doesn't hold); this endpoint only persists what it's given. Every
/// goal lands in the same project as <see cref="CreateGoalEndpoint"/> uses (the given
/// <see cref="CreateCombinedGoalsRequest.ProjectId"/>, or the caller's default project).
/// </summary>
public sealed class CreateCombinedGoalsEndpoint
    : Endpoint<CreateCombinedGoalsRequest, CreateCombinedGoalsResponse, GoalMapper>
{
    public override void Configure()
    {
        Post("me/goals/combined");
        Summary(summary =>
        {
            summary.Summary = "Creates several linked goals for one entity in one call (combined creation).";
            summary.Description = "Every goal shares one AggregateId; each spec's DependsOnIndex (indices "
                + "into this same request's Goals list, referencing only earlier entries) is resolved into "
                + "real DependsOn edges. Rank goals get their milestone breakpoints generated. Assigns the "
                + "whole set to the given project, or the caller's default project (created on first use) "
                + "when none is given, same as POST me/goals.";
            summary.Response<CreateCombinedGoalsResponse>(StatusCodes.Status200OK, "The newly created goals, in request order.");
            summary.Response(StatusCodes.Status400BadRequest, "Invalid entity/goal type, a bad DependsOnIndex, or an unknown project.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated account/profile has not been provisioned.");
        });
    }

    public override async Task HandleAsync(CreateCombinedGoalsRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var projects = Resolve<ProjectsService>();

        var profile = await db.Profiles.FirstAsync(entity => entity.Id == profileId, ct);

        Project project;
        if (req.ProjectId is { } requestedProjectId)
        {
            var found = await db.Projects.Owned(profileId)
                .FirstOrDefaultAsync(entity => entity.Id == ProjectId.From(requestedProjectId), ct);
            if (found is null)
            {
                AddError(request => request.ProjectId, "Unknown project.");
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
            }

            project = found;
        }
        else
        {
            project = await projects.EnsureDefaultProjectAsync(profileId, ct);
        }

        var status = project.Id == profile.ActiveProjectId ? GoalStatus.Active : GoalStatus.Paused;
        var aggregateId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var nextPriority = await projects.GetNextPriorityAsync(project.Id, ct);

        // First pass: build every goal (so each spec's index has a real GoalId to resolve dependencies
        // against), deferring DependsOn assignment to the second pass below.
        var goals = new List<Goal>(req.Goals.Count);
        foreach (var spec in req.Goals)
        {
            var goal = new Goal
            {
                Id = GoalId.From(Guid.CreateVersion7()),
                ProfileId = profileId,
                EntityType = Enum.Parse<GoalEntityType>(req.EntityType, ignoreCase: true),
                EntityId = req.EntityId.Trim(),
                GoalType = Enum.Parse<GoalType>(spec.GoalType, ignoreCase: true),
                Status = status,
                Config = GoalMapper.MapConfig(spec.Config),
                AggregateId = aggregateId,
                Events = [new GoalEvent { At = now, Type = GoalEventType.Created }],
            };

            if (goal.GoalType == GoalType.Rank && goal.Config.Rank is { } rank)
            {
                goal.Milestones = MilestoneGenerator.ForRank(rank.Start, rank.End);
            }

            goals.Add(goal);
        }

        for (var i = 0; i < req.Goals.Count; i++)
        {
            goals[i].DependsOn = req.Goals[i].DependsOnIndex.Select(index => goals[index].Id.Value).ToList();
        }

        db.Goals.AddRange(goals);
        db.ProjectGoals.AddRange(goals.Select((goal, i) => new ProjectGoal
        {
            ProjectId = project.Id,
            GoalId = goal.Id,
            Priority = nextPriority + i,
            CreatedAt = now,
        }));

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new CreateCombinedGoalsResponse(goals.Select(Map.FromEntity).ToList()), ct);
    }
}

public sealed record CreateCombinedGoalsRequest(
    string EntityType,
    string EntityId,
    Guid? ProjectId,
    List<CombinedGoalSpec> Goals
);

/// <summary>One goal within a combined-creation request. <see cref="DependsOnIndex"/> holds indices into
/// the parent request's <see cref="CreateCombinedGoalsRequest.Goals"/> list — each must reference a
/// strictly earlier position (validated by <see cref="CreateCombinedGoalsValidator"/>), resolved into real
/// <see cref="Goal.DependsOn"/> ids by the endpoint.</summary>
public sealed record CombinedGoalSpec(
    string GoalType,
    CreateGoalConfigRequest Config,
    List<int> DependsOnIndex
);

public sealed record CreateCombinedGoalsResponse(List<GoalDetailResponse> Goals);
