using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.GameDomain;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Creates one goal, or several linked goals for one entity, in a single call (the only creation route). For the
/// combined flow the client composes any combination of Unlock/Ascension/Rank/Ability/Upgrade targets, detects
/// unmet prerequisites (it holds the live player data) and orders the request accordingly; this endpoint persists
/// the whole set atomically, each spec's <see cref="CombinedGoalSpec.DependsOnIndex"/> resolved into real
/// <see cref="Goal.DependsOn"/> edges. Every goal lands in the given projects, or the caller's default project.
/// </summary>
public sealed class CreateCombinedGoalsEndpoint
    : Endpoint<CreateCombinedGoalsRequest, CreateCombinedGoalsResponse, GoalMapper>
{
    public override void Configure()
    {
        Post("goals");
        Summary(summary =>
        {
            summary.Summary = "Creates one goal, or several linked goals for one entity, in one call.";
            summary.Description = "A single goal is a one-spec request. Each spec's DependsOnIndex (indices into "
                + "this same request's Goals list, referencing only earlier entries) is resolved into real DependsOn "
                + "edges. Assigns the whole set to the given project(s), or the caller's default project (created on "
                + "first use) when none is given. Every goal starts Active; startPaused (request-level boolean, "
                + "default false) creates the whole set Paused instead and cannot be set per goal.";
            summary.Response<CreateCombinedGoalsResponse>(StatusCodes.Status200OK, "The newly created goals, in request order.");
            summary.Response(StatusCodes.Status400BadRequest,
                "Invalid entity/goal type, a bad DependsOnIndex, an unknown project, or a null/per-goal startPaused.");
            summary.Response(StatusCodes.Status409Conflict,
                "slotOccupied: an active or paused goal already holds one of the requested slots (current: that goal).");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated account/profile has not been provisioned.");
        });
    }

    public override async Task HandleAsync(CreateCombinedGoalsRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var projects = Resolve<ProjectsService>();
        var targetValidation = Resolve<GoalTargetValidationService>();
        var entityType = req.EntityType;

        // One roster read for every spec in the request.
        var units = await targetValidation.LoadUnitsAsync(profileId, req.EntityId.Trim(), ct);
        var requestGoalTypes = new HashSet<GoalType>();
        var parsedGoalTypes = new List<GoalType>(req.Goals.Count);
        // Each spec's transitive dependency closure, computed in one forward pass — DependsOnIndex is
        // guaranteed (by CreateCombinedGoalsValidator) to reference only strictly earlier indices, so
        // closures[d] for every d in a spec's direct deps is already complete by the time we reach it.
        var closures = new List<HashSet<int>>(req.Goals.Count);
        for (var i = 0; i < req.Goals.Count; i++)
        {
            var spec = req.Goals[i];
            var goalType = spec.GoalType;
            parsedGoalTypes.Add(goalType);

            var directDeps = spec.DependsOnIndex ?? [];
            var closure = new HashSet<int>(directDeps);
            foreach (var dependencyIndex in directDeps)
                closure.UnionWith(closures[dependencyIndex]);
            closures.Add(closure);

            // The lift is gated on a declared dependency (goal-target-model): only Ascension specs within
            // this spec's own closure count, not every Ascension spec anywhere in the request.
            var ascensionEndIndices = closure
                .Where(dependencyIndex => parsedGoalTypes[dependencyIndex] == GoalType.Ascension)
                .Select(dependencyIndex => req.Goals[dependencyIndex].Config.Progression)
                .OfType<ProgressionTargetRequest>()
                .Select(progression => (int)progression.End)
                .ToList();
            UnitProgression? effectiveProgressionFloor =
                ascensionEndIndices.Count > 0 ? (UnitProgression)ascensionEndIndices.Max() : null;

            if (targetValidation.Validate(
                units, entityType, req.EntityId.Trim(), goalType, spec.Config, effectiveProgressionFloor)
                is { } targetError)
            {
                AddError(targetError);
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
            }

            // A combined request can't ask for two goals of the same type for the same entity either —
            // same "at most one in flight" invariant as a single goal type would need against itself.
            if (!requestGoalTypes.Add(goalType))
            {
                AddError(request => request.Goals, "Each goal type may appear at most once in a combined request.");
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
            }
        }

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

        // Request-wide, not per spec: the set is one dependency chain, so a Paused prerequisite under an
        // Active dependent is a state nothing needs.
        var status = req.StartPaused ? GoalStatus.Paused : GoalStatus.Active;

        // DependsOn references another spec's GoalId, only resolvable once every goal in the set has one.
        // CreateCombinedGoalsValidator guarantees strictly-earlier indices; re-checked here rather than trusting an
        // invariant enforced elsewhere.
        var goals = req.Goals.Select((spec, i) => new Goal(entityType, req.EntityId.Trim(), parsedGoalTypes[i])
        {
            Id = GoalId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            Status = status,
            Config = GoalMapper.MapConfig(spec.Config),
            Snapshot = GoalMapper.MapSnapshot(spec.Snapshot),
        }).ToList();
        for (var i = 0; i < req.Goals.Count; i++)
        {
            var indices = req.Goals[i].DependsOnIndex ?? [];
            if (indices.Any(index => index < 0 || index >= i))
            {
                AddError(request => request.Goals, "DependsOnIndex must reference an earlier goal in this request.");
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
            }

            goals[i].DependsOn = indices.Select(index => goals[index].Id.Value).ToList();
            goals[i].RefreshRankTargetKey();
        }

        var now = Resolve<TimeProvider>().GetUtcNow();
        var planning = Resolve<ProjectGoalPlanningService>();
        await planning.ExecuteLockedMutationAsync(async transaction =>
        {
            // At most one Active/Paused goal per slot of the profile (Rank: per normalized end target).
            if (await planning.FindConflictAsync(goals, ct) is { } existingConflict)
            {
                await GoalProblems.SendSlotConflictAsync(this, existingConflict, ct);
                return;
            }

            db.Goals.AddRange(goals);
            // Once each, in request order, however many projects they are filed into.
            var order = Resolve<GoalOrderService>();
            foreach (var goal in goals)
                await order.AppendAsync(goal, ct);
            foreach (var project in targetProjects)
                db.ProjectGoals.AddRange(goals.Select(goal => ProjectGoalPlanningService.CreateMembership(project.Id, goal.Id, now)));

            try
            {
                await order.CompleteAsync(ct);
            }
            catch (DbUpdateException ex) when (ProjectGoalPlanningService.IsSlotViolation(ex))
            {
                await GoalProblems.SendSlotConflictAsync(
                    this, await planning.FindConflictAfterFailedSaveAsync(transaction, goals, ex, ct), ct);
                return;
            }

            await transaction.CommitAsync(ct);

            var projectIds = targetProjects.Select(project => project.Id.Value).ToList();
            await Send.OkAsync(
                new CreateCombinedGoalsResponse(goals.Select(goal => Map.ToDetail(goal, projectIds)).ToList()), ct);
        }, ct);
    }
}

public sealed record CreateCombinedGoalsRequest(
    [property: JsonRequired] GoalEntityType EntityType,
    string EntityId,
    List<ProjectPriorityRequest>? Projects,
    List<CombinedGoalSpec> Goals,
    // Non-nullable: an explicit null fails binding with a 400 naming startPaused.
    bool StartPaused = false
);

/// <summary>One goal within a creation request. Unknown members (notably a per-goal <c>startPaused</c>) are
/// rejected, not ignored. <see cref="DependsOnIndex"/> holds indices into
/// the parent request's <see cref="CreateCombinedGoalsRequest.Goals"/> list — each must reference a
/// strictly earlier position (validated by <see cref="CreateCombinedGoalsValidator"/>), resolved into real
/// <see cref="Goal.DependsOn"/> ids by the endpoint.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CombinedGoalSpec(
    [property: JsonRequired] GoalType GoalType,
    CreateGoalConfigRequest Config,
    List<int> DependsOnIndex,
    CreateGoalSnapshotRequest? Snapshot = null
);

public sealed record CreateCombinedGoalsResponse(List<GoalDetailResponse> Goals);

/// <summary>One target project a newly created goal is filed into. A goal takes no caller-chosen
/// priority: it appends once to the account-wide in-flight order however many projects it joins.</summary>
public sealed record ProjectPriorityRequest(Guid ProjectId);

public sealed record CreateGoalConfigRequest(
    RankTargetRequest? Rank = null,
    ProgressionTargetRequest? Progression = null,
    AbilityTargetRequest? Ability = null,
    List<CampaignBattleId>? FarmingLocationIds = null,
    FarmingStrategy? FarmingStrategy = null,
    List<AcquisitionSourceRequest>? AcquisitionSources = null,
    UpgradeTargetRequest? Upgrade = null
);

/// <summary>One selected shard acquisition source (plan: multi-select Campaigns/Onslaught/Shops picker).
/// <see cref="Kind"/> must be one of <see cref="Domain.Goals.AcquisitionSourceKinds"/>; <see cref="Ids"/>
/// carries campaign battle ids for <c>Campaign</c>, shop-offer ids
/// (<c>&lt;shopId&gt;:&lt;rewardType&gt;</c>) for <c>Shop</c>, and must be empty for <c>Onslaught</c>.
/// Kept as plain strings (not <see cref="CampaignBattleId"/>) because the id type varies by kind; the
/// campaign ids are validated against the character's shard nodes in
/// <see cref="GoalTargetValidationService"/>.</summary>
public sealed record AcquisitionSourceRequest([property: JsonRequired] AcquisitionSourceKind Kind, List<string> Ids);

public sealed record RankTargetRequest(
    int Start,
    bool StartPointFive,
    int StartAppliedUpgrades,
    int End,
    bool EndPointFive,
    int EndAppliedUpgrades
);

public sealed record ProgressionTargetRequest(
    [property: JsonRequired] UnitProgression Start,
    [property: JsonRequired] UnitProgression End);

public sealed record AbilityTargetRequest(int ActiveStart, int ActiveEnd, int PassiveStart, int PassiveEnd);

/// <summary>Optional progression ranges the goal was created against: <c>RankRange</c> (Character only) is a
/// rank-ladder index pair (<c>UnitRank</c> ordinal, e.g. Stone2 = 1); <c>ActiveRange</c>/<c>PassiveRange</c>
/// (Mow only) are ability-level pairs. Omitted groups stay null — no overlap de-duplication.</summary>
public sealed record UpgradeTargetRequest(
    List<UpgradeMaterialTargetRequest> Targets,
    UpgradeRangeRequest? RankRange = null,
    UpgradeRangeRequest? ActiveRange = null,
    UpgradeRangeRequest? PassiveRange = null
);

/// <summary>Nullable members so a half-present group is detectable (and rejected with a 400) rather than
/// silently defaulting to 0.</summary>
public sealed record UpgradeRangeRequest(int? Start, int? End);

public sealed record UpgradeMaterialTargetRequest(string UpgradeId, int Quantity);

public sealed record CreateGoalSnapshotRequest(
    UnitRank? InitialRank = null,
    UnitProgression? InitialProgression = null,
    int? InitialActiveAbilityLevel = null,
    int? InitialPassiveAbilityLevel = null,
    List<GoalSnapshotResourceRequest>? InitialRequirement = null,
    List<GoalSnapshotResourceRequest>? InitialInventoryContribution = null
);

public sealed record GoalSnapshotResourceRequest(string ResourceId, int Count);
