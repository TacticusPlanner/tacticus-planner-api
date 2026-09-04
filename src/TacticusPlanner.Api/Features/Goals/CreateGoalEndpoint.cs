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
            summary.Summary = "Creates a goal for a character or Machine of War.";
            summary.Description = "Assigns the goal to the given project(s), or the caller's default project "
                + "(created on first use) when none is given. The goal starts Active if any target project is "
                + "the caller's active plan, otherwise Paused.";
            summary.Response<GoalDetailResponse>(StatusCodes.Status200OK, "The newly created goal.");
            summary.Response(StatusCodes.Status400BadRequest, "Invalid entity/goal type, or an unknown project.");
            summary.Response<ProjectGoalSlotConflictResponse>(StatusCodes.Status409Conflict,
                "A target project already contains an active or paused goal in the requested slot.");
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
        var profile = await db.Profiles.FirstAsync(entity => entity.Id == profileId, ct);

        List<Project> targetProjects;
        if (req.Projects is { Count: > 0 } requestedProjects)
        {
            var distinctIds = requestedProjects.Select(entry => entry.ProjectId).Distinct().Select(ProjectId.From).ToList();
            var found = await db.Projects
                .Where(entity => distinctIds.Contains(entity.Id))
                .ToListAsync(ct);
            if (found.Count != distinctIds.Count)
            {
                AddError(request => request.Projects, "Unknown project.");
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
            }

            targetProjects = found;
        }
        else
        {
            targetProjects = [await projects.EnsureDefaultProjectAsync(profileId, ct)];
        }

        var goal = Map.ToEntity(req);
        goal.Id = GoalId.From(Guid.CreateVersion7());
        goal.ProfileId = profileId;
        goal.Status = targetProjects.Any(project => project.Id == profile.ActiveProjectId)
            ? GoalStatus.Active
            : GoalStatus.Paused;
        var now = DateTimeOffset.UtcNow;
        goal.Snapshot = GoalMapper.MapSnapshot(req.Snapshot);
        goal.Events = [new GoalEvent { At = now, Type = GoalEventType.Created }];

        var planning = Resolve<ProjectGoalPlanningService>();
        await planning.ExecuteLockedMutationAsync(
            targetProjects.Select(project => project.Id),
            async transaction =>
        {
            if (await planning.FindConflictAsync(
                targetProjects.Select(project => project.Id), entityType, goal.EntityId, goalType, null, ct) is { } conflict)
            {
                await SendSlotConflictAsync(conflict, ct);
                return;
            }

            db.Goals.Add(goal);

            foreach (var project in targetProjects)
            {
                db.ProjectGoals.Add(ProjectGoalPlanningService.CreateMembership(
                    project, goal, await projects.GetNextPriorityAsync(project.Id, ct), now));
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (GoalConflictDetection.IsProjectSlotConflict(ex))
            {
                var databaseConflict = await planning.FindConflictAfterFailedSaveAsync(
                    transaction,
                    [new ProjectGoalSlotLookup(
                    targetProjects.Select(project => project.Id).ToList(),
                    entityType,
                    goal.EntityId,
                    goalType)],
                    ct) ?? throw new InvalidOperationException(
                        "The project slot constraint failed but no conflicting membership was found.", ex);
                await SendSlotConflictAsync(databaseConflict, ct);
                return;
            }

            await planning.NormalizeAsync(targetProjects.Select(project => project.Id), ct);
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);

            var projectIds = targetProjects.Select(project => project.Id.Value).ToList();
            await Send.OkAsync(Map.ToDetail(goal, projectIds), ct);
        }, ct);
    }

    private async Task SendSlotConflictAsync(ProjectGoalSlotConflictResponse conflict, CancellationToken ct)
    {
        HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        await HttpContext.Response.WriteAsJsonAsync(conflict, ct);
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
public sealed record ProjectPriorityRequest(Guid ProjectId);

public sealed record CreateGoalConfigRequest(
    RankTargetRequest? Rank = null,
    ProgressionTargetRequest? Progression = null,
    AbilityTargetRequest? Ability = null,
    List<CampaignBattleId>? FarmingLocationIds = null,
    string? FarmingStrategy = null,
    List<AcquisitionSourceRequest>? AcquisitionSources = null,
    UpgradeTargetRequest? Upgrade = null,
    LevelTargetRequest? Level = null
);

/// <summary>One selected shard acquisition source (plan: multi-select Campaigns/Onslaught/Shops picker).
/// <see cref="Kind"/> must be one of <see cref="Domain.Goals.AcquisitionSourceKinds"/>; <see cref="Ids"/>
/// carries campaign battle ids for <c>Campaign</c>, shop-offer ids
/// (<c>&lt;shopId&gt;:&lt;rewardType&gt;</c>) for <c>Shop</c>, and must be empty for <c>Onslaught</c>.
/// Kept as plain strings (not <see cref="CampaignBattleId"/>) because the id type varies by kind; the
/// campaign ids are validated against the character's shard nodes in
/// <see cref="GoalTargetValidationService"/>.</summary>
public sealed record AcquisitionSourceRequest(string Kind, List<string> Ids);

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

public sealed record UpgradeTargetRequest(List<UpgradeMaterialTargetRequest> Targets);

public sealed record UpgradeMaterialTargetRequest(string UpgradeId, int Quantity);

public sealed record LevelTargetRequest(int Start, int End);

/// <summary><see cref="InitialRank"/>/<see cref="InitialProgression"/> are the client's plain wire strings
/// (e.g. "Gold2", "Common:TwoStars" — the same values <c>Rank</c>/<c>Progression</c> serialize to); an
/// unparseable value is dropped rather than rejected, see <c>GoalMapper.MapSnapshot</c>.</summary>
public sealed record CreateGoalSnapshotRequest(
    string? InitialRank = null,
    string? InitialProgression = null,
    int? InitialActiveAbilityLevel = null,
    int? InitialPassiveAbilityLevel = null,
    List<GoalSnapshotResourceRequest>? InitialRequirement = null,
    List<GoalSnapshotResourceRequest>? InitialInventoryContribution = null
);

public sealed record GoalSnapshotResourceRequest(string ResourceId, int Count);
