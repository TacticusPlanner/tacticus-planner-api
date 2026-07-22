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
/// <see cref="CreateGoalRequest.Projects"/> is omitted or empty, the caller's default project is used
/// (created on first access); otherwise the goal is added to every listed project (a goal may belong to
/// several projects at once), at that project's given <see cref="ProjectPriorityRequest.Priority"/> when
/// supplied, or appended after the project's current goals otherwise. The combined-creation flow (multiple
/// goal types + dependency chains for one entity, plan §6/§8) is not implemented here — this endpoint only
/// ever creates one independent goal.
/// </summary>
public sealed class CreateGoalEndpoint : Endpoint<CreateGoalRequest, GoalDetailResponse, GoalMapper>
{
    public override void Configure()
    {
        Post("me/goals");
        Summary(summary =>
        {
            summary.Summary = "Creates a goal for a character, Machine of War, or equipment.";
            summary.Description = "Assigns the goal to the given project(s), or the caller's default project "
                + "(created on first use) when none is given. The goal starts Active if any target project is "
                + "the caller's active plan, otherwise Paused.";
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
        var targetValidation = Resolve<GoalTargetValidationService>();

        var entityType = Enum.Parse<GoalEntityType>(req.EntityType, ignoreCase: true);
        var goalType = Enum.Parse<GoalType>(req.GoalType, ignoreCase: true);
        if (await targetValidation.ValidateAsync(profileId, entityType, req.EntityId.Trim(), goalType, req.Config, ct) is { } targetError)
        {
            AddError(targetError);
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        // At most one Active/Paused goal per (entity, goal type) — a unit may still accumulate several
        // Completed/Archived goals of the same type, but only one "in flight" at a time (see the mirrored
        // check in UpdateGoalStatusEndpoint, and the partial unique index backing this invariant).
        var hasConflictingGoal = await db.Goals.Owned(profileId)
            .Where(entity => entity.EntityType == entityType
                && entity.EntityId == req.EntityId.Trim()
                && entity.GoalType == goalType
                && (entity.Status == GoalStatus.Active || entity.Status == GoalStatus.Paused))
            .AnyAsync(ct);
        if (hasConflictingGoal)
        {
            AddError(request => request.GoalType, "An active or paused goal of this type already exists for this unit.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var profile = await db.Profiles.FirstAsync(entity => entity.Id == profileId, ct);

        List<Project> targetProjects;
        Dictionary<ProjectId, int> requestedPriorities = [];
        if (req.Projects is { Count: > 0 } requestedProjects)
        {
            var distinctIds = requestedProjects.Select(entry => entry.ProjectId).Distinct().Select(ProjectId.From).ToList();
            var found = await db.Projects.Owned(profileId)
                .Where(entity => distinctIds.Contains(entity.Id))
                .ToListAsync(ct);
            if (found.Count != distinctIds.Count)
            {
                AddError(request => request.Projects, "Unknown project.");
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
            }

            targetProjects = found;
            foreach (var entry in requestedProjects)
            {
                if (entry.Priority is { } priority)
                {
                    requestedPriorities[ProjectId.From(entry.ProjectId)] = priority;
                }
            }
        }
        else
        {
            targetProjects = [await projects.EnsureDefaultProjectAsync(profileId, ct)];
        }

        var goal = Map.ToEntity(req);
        goal.Id = GoalId.From(Guid.CreateVersion7());
        goal.ProfileId = profileId;
        goal.EntityType = entityType;
        goal.GoalType = goalType;
        goal.Status = targetProjects.Any(project => project.Id == profile.ActiveProjectId)
            ? GoalStatus.Active
            : GoalStatus.Paused;
        var now = DateTimeOffset.UtcNow;
        goal.Snapshot = GoalMapper.MapSnapshot(req.Snapshot, now);
        goal.Events = [new GoalEvent { At = now, Type = GoalEventType.Created }];
        if (goal.GoalType == GoalType.Rank && goal.Config.Rank is { } rank)
        {
            goal.Milestones = MilestoneGenerator.ForRank(rank.Start, rank.End, goal.Config.FarmingStrategy);
        }
        else if (goal.EntityType == GoalEntityType.Mow
            && goal.GoalType == GoalType.Ability
            && goal.Config.Ability is { } ability)
        {
            var (start, end) = ability.ActiveEnd > ability.ActiveStart
                ? (ability.ActiveStart, ability.ActiveEnd)
                : (ability.PassiveStart, ability.PassiveEnd);
            goal.Milestones = MilestoneGenerator.ForAbility(start, end, goal.Config.FarmingStrategy);
        }

        db.Goals.Add(goal);

        foreach (var project in targetProjects)
        {
            db.ProjectGoals.Add(new ProjectGoal
            {
                ProjectId = project.Id,
                GoalId = goal.Id,
                Priority = requestedPriorities.TryGetValue(project.Id, out var priority)
                    ? priority
                    : await projects.GetNextPriorityAsync(project.Id, ct),
            });
        }

        await db.SaveChangesAsync(ct);

        var projectIds = targetProjects.Select(project => project.Id.Value).ToList();
        await Send.OkAsync(Map.ToDetail(goal, projectIds), ct);
    }
}

public sealed record CreateGoalRequest(
    string EntityType,
    string EntityId,
    string GoalType,
    CreateGoalConfigRequest Config,
    List<ProjectPriorityRequest>? Projects,
    CreateGoalSnapshotRequest? Snapshot = null
);

/// <summary>One target project for a newly created goal, with an optional caller-chosen priority within
/// that project (plan: per-project priority). <see cref="Priority"/> null means "append after the
/// project's current goals" — the same behavior as before per-project priority existed.</summary>
public sealed record ProjectPriorityRequest(Guid ProjectId, int? Priority);

public sealed record CreateGoalConfigRequest(
    RankTargetRequest? Rank = null,
    ProgressionTargetRequest? Progression = null,
    AbilityTargetRequest? Ability = null,
    List<CampaignBattleId>? FarmingLocationIds = null,
    string? FarmingStrategy = null,
    AscensionFarmingRequest? AscensionFarming = null,
    UpgradeTargetRequest? Upgrade = null,
    EquipmentTargetRequest? Equipment = null,
    LevelTargetRequest? Level = null
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

public sealed record AscensionFarmingRequest(
    string Source,
    List<CampaignBattleId> ShardBattleIds,
    List<CampaignBattleId> MythicShardBattleIds
);

public sealed record UpgradeTargetRequest(List<UpgradeItemTargetRequest> Targets);

public sealed record UpgradeItemTargetRequest(string UpgradeId, int Quantity);

public sealed record EquipmentTargetRequest(int TargetLevel);

public sealed record LevelTargetRequest(int Start, int End);

public sealed record CreateGoalSnapshotRequest(
    string? InitialRank = null,
    string? InitialProgression = null,
    int? InitialActiveAbilityLevel = null,
    int? InitialPassiveAbilityLevel = null,
    bool? InitialUnlocked = null,
    List<GoalSnapshotResourceRequest>? InitialRequirement = null,
    List<GoalSnapshotResourceRequest>? InitialInventoryContribution = null,
    int? OriginalEnergyTotal = null,
    int? OriginalRaidsTotal = null,
    int? OriginalEstimateDays = null,
    DateTimeOffset? OriginalEstimateDate = null
);

public sealed record GoalSnapshotResourceRequest(string ResourceId, int Count);
