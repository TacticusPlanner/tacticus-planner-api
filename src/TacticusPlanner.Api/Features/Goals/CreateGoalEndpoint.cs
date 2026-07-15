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
public sealed class CreateGoalEndpoint : Endpoint<CreateGoalRequest, GoalDetailResponse, GoalMapper>
{
    public override void Configure()
    {
        Post("me/goals");
        Summary(summary =>
        {
            summary.Summary = "Creates a goal for a character, Machine of War, or (reserved) upgrade material.";
            summary.Description = "Assigns the goal to the given project, or the caller's default project "
                + "(created on first use) when none is given. The goal starts Active if that project is the "
                + "caller's active plan, otherwise Paused. Upgrade-entity and material-goal types are "
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

        var goal = Map.ToEntity(req);
        goal.Id = GoalId.From(Guid.CreateVersion7());
        goal.ProfileId = profileId;
        goal.EntityType = Enum.Parse<GoalEntityType>(req.EntityType, ignoreCase: true);
        goal.GoalType = Enum.Parse<GoalType>(req.GoalType, ignoreCase: true);
        goal.Status = project.Id == profile.ActiveProjectId ? GoalStatus.Active : GoalStatus.Paused;
        var now = DateTimeOffset.UtcNow;
        goal.Snapshot = GoalMapper.MapSnapshot(req.Snapshot, now);
        goal.Events = [new GoalEvent { At = now, Type = GoalEventType.Created }];

        db.Goals.Add(goal);

        db.ProjectGoals.Add(new ProjectGoal
        {
            ProjectId = project.Id,
            GoalId = goal.Id,
            Priority = await projects.GetNextPriorityAsync(project.Id, ct),
        });

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(Map.FromEntity(goal), ct);
    }
}

public sealed record CreateGoalRequest(
    string EntityType,
    string EntityId,
    string GoalType,
    CreateGoalConfigRequest Config,
    Guid? ProjectId,
    CreateGoalSnapshotRequest? Snapshot = null
);

public sealed record CreateGoalConfigRequest(
    RankTargetRequest? Rank = null,
    ProgressionTargetRequest? Progression = null,
    AbilityTargetRequest? Ability = null,
    ShardTargetRequest? Shards = null,
    List<CampaignBattleId>? FarmingLocationIds = null
);

public sealed record RankTargetRequest(
    int Start,
    bool StartPointFive,
    int StartAppliedUpgrades,
    int End,
    bool EndPointFive,
    int EndAppliedUpgrades
);

public sealed record ProgressionTargetRequest(string Start, string End);

public sealed record AbilityTargetRequest(int ActiveStart, int ActiveEnd, int PassiveStart, int PassiveEnd);

public sealed record ShardTargetRequest(int Count);

public sealed record CreateGoalSnapshotRequest(
    string? InitialRank = null,
    string? InitialProgression = null,
    int? InitialActiveAbilityLevel = null,
    int? InitialPassiveAbilityLevel = null,
    int? InitialShards = null,
    bool? InitialUnlocked = null,
    List<GoalSnapshotResourceRequest>? InitialRequirement = null,
    List<GoalSnapshotResourceRequest>? InitialInventoryContribution = null,
    int? OriginalEnergyTotal = null,
    int? OriginalRaidsTotal = null,
    int? OriginalEstimateDays = null,
    DateTimeOffset? OriginalEstimateDate = null
);

public sealed record GoalSnapshotResourceRequest(string ResourceId, int Count);
