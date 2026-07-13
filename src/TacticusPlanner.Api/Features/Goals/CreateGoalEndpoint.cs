using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Creates a single goal. Every goal must belong to at least one project (plan §5): if
/// <see cref="CreateGoalRequest.ProjectId"/> is omitted, the caller's default project is used (created on
/// first access). The combined-creation flow (multiple goal types + dependency chains for one entity, plan
/// §6/§8) is not implemented yet — this endpoint only ever creates one independent goal.
/// </summary>
public sealed class CreateGoalEndpoint : Endpoint<CreateGoalRequest, GoalDetailResponse>
{
    public override void Configure()
    {
        Post("me/goals");
        Summary(summary =>
        {
            summary.Summary = "Creates a goal for a character, Machine of War, or (reserved) upgrade material.";
            summary.Description = "Assigns the goal to the given project, or the caller's default project "
                + "(created on first use) when none is given. Upgrade-entity and material-goal types are "
                + "reserved for a later phase and are rejected here.";
            summary.Response<GoalDetailResponse>(StatusCodes.Status200OK, "The newly created goal.");
            summary.Response(StatusCodes.Status400BadRequest, "Invalid entity/goal type, or an unknown project.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated account/profile has not been provisioned.");
        });
    }

    public override async Task HandleAsync(CreateGoalRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!Enum.TryParse<GoalEntityType>(req.EntityType, ignoreCase: true, out var entityType)
            || entityType == GoalEntityType.Upgrade)
        {
            AddError(request => request.EntityType, "Unknown or not-yet-supported entity type.");
        }

        if (!Enum.TryParse<GoalType>(req.GoalType, ignoreCase: true, out var goalType)
            || goalType == GoalType.Material)
        {
            AddError(request => request.GoalType, "Unknown or not-yet-supported goal type.");
        }

        if (string.IsNullOrWhiteSpace(req.EntityId))
        {
            AddError(request => request.EntityId, "An entity id is required.");
        }

        ThrowIfAnyErrors();

        var db = Resolve<PlannerDbContext>();
        var projects = Resolve<ProjectsService>();

        Project project;
        if (req.ProjectId is { } requestedProjectId)
        {
            var found = await db.Projects.FirstOrDefaultAsync(
                entity => entity.Id == ProjectId.From(requestedProjectId) && entity.ProfileId == profileId,
                ct
            );
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

        var goal = new Goal
        {
            Id = GoalId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            EntityType = entityType,
            EntityId = req.EntityId!.Trim(),
            GoalType = goalType,
            Status = GoalStatus.Active,
            Config = MapConfig(req.Config),
            Events = [new GoalEvent { At = DateTimeOffset.UtcNow, Type = "created" }],
        };

        db.Goals.Add(goal);

        db.ProjectGoals.Add(new ProjectGoal
        {
            ProjectId = project.Id,
            GoalId = goal.Id,
            Priority = await projects.GetNextPriorityAsync(project.Id, ct),
        });

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(GoalProjection.BuildDetail(goal), ct);
    }

    private static GoalConfig MapConfig(CreateGoalConfigRequest config) => new()
    {
        RankStart = config.RankStart,
        RankStartPointFive = config.RankStartPointFive,
        RankStartAppliedUpgrades = config.RankStartAppliedUpgrades,
        RankEnd = config.RankEnd,
        RankEndPointFive = config.RankEndPointFive,
        RankEndAppliedUpgrades = config.RankEndAppliedUpgrades,
        ProgressionStart = config.ProgressionStart,
        ProgressionEnd = config.ProgressionEnd,
        AbilityActiveStart = config.AbilityActiveStart,
        AbilityActiveEnd = config.AbilityActiveEnd,
        AbilityPassiveStart = config.AbilityPassiveStart,
        AbilityPassiveEnd = config.AbilityPassiveEnd,
        ShardsTarget = config.ShardsTarget,
        FarmingMode = config.FarmingMode,
        FarmingLocationIds = config.FarmingLocationIds,
    };
}

public sealed record CreateGoalRequest(
    string EntityType,
    string EntityId,
    string GoalType,
    CreateGoalConfigRequest Config,
    Guid? ProjectId
);

public sealed record CreateGoalConfigRequest(
    int? RankStart = null,
    bool? RankStartPointFive = null,
    int? RankStartAppliedUpgrades = null,
    int? RankEnd = null,
    bool? RankEndPointFive = null,
    int? RankEndAppliedUpgrades = null,
    string? ProgressionStart = null,
    string? ProgressionEnd = null,
    int? AbilityActiveStart = null,
    int? AbilityActiveEnd = null,
    int? AbilityPassiveStart = null,
    int? AbilityPassiveEnd = null,
    int? ShardsTarget = null,
    string? FarmingMode = null,
    List<string>? FarmingLocationIds = null
);
