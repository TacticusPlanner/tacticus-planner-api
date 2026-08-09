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
/// each spec's <see cref="CombinedGoalSpec.DependsOnIndex"/> resolved into real <see cref="Goal.DependsOn"/>
/// edges. Detection/ordering itself stays client-side (it needs live player-data the server doesn't hold);
/// this endpoint only persists what it's given. Every goal lands in the same project(s) as
/// <see cref="CreateGoalEndpoint"/> uses (the given <see cref="CreateCombinedGoalsRequest.Projects"/>, or
/// the caller's default project) — a given per-project <see cref="ProjectPriorityRequest.Priority"/>
/// becomes that project's base priority for the whole set, with each subsequent goal in the set placed
/// immediately after (same "+i" spacing used when no priority is given).
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
            summary.Description = "Each spec's DependsOnIndex (indices into this same request's Goals list, "
                + "referencing only earlier entries) is resolved into real DependsOn edges. Assigns the "
                + "whole set to the given project(s), or the caller's default project (created on first use) "
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
        var targetValidation = Resolve<GoalTargetValidationService>();
        if (!Enum.TryParse<GoalEntityType>(req.EntityType, ignoreCase: true, out var entityType))
        {
            AddError(request => request.EntityType, "Unknown entity type.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var requestGoalTypes = new HashSet<GoalType>();
        var parsedGoalTypes = new List<GoalType>(req.Goals.Count);
        foreach (var spec in req.Goals)
        {
            if (!Enum.TryParse<GoalType>(spec.GoalType, ignoreCase: true, out var goalType))
            {
                AddError(request => request.Goals, "Unknown or not-yet-supported goal type.");
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
            }

            parsedGoalTypes.Add(goalType);
            if (await targetValidation.ValidateAsync(
                profileId, entityType, req.EntityId.Trim(), goalType, spec.Config, ct) is { } targetError)
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

        // At most one Active/Paused goal per (entity, goal type) — mirrors CreateGoalEndpoint's check.
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

        var status = targetProjects.Any(project => project.Id == profile.ActiveProjectId)
            ? GoalStatus.Active
            : GoalStatus.Paused;
        var now = DateTimeOffset.UtcNow;
        var planning = Resolve<ProjectGoalPlanningService>();
        await using var transaction = await planning.BeginLockedMutationAsync(
            targetProjects.Select(project => project.Id), ct);
        foreach (var goalType in requestGoalTypes)
        {
            if (await planning.FindConflictAsync(
                targetProjects.Select(project => project.Id), entityType, req.EntityId.Trim(), goalType, null, ct) is { } conflict)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                await HttpContext.Response.WriteAsJsonAsync(conflict, ct);
                return;
            }
        }

        // Built as a materialized, indexable List (not a lazy .Select) so each spec's index has a real
        // GoalId to resolve dependencies against in the second pass below.
        var goals = req.Goals.Select((spec, i) => new Goal
        {
            Id = GoalId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            EntityType = entityType,
            EntityId = req.EntityId.Trim(),
            GoalType = parsedGoalTypes[i],
            Status = status,
            Config = GoalMapper.MapConfig(spec.Config),
            Snapshot = GoalMapper.MapSnapshot(spec.Snapshot),
            Events = [new GoalEvent { At = now, Type = GoalEventType.Created }],
        }).ToList();

        // Second pass: DependsOn references another spec's GoalId, only resolvable once every goal in
        // the set already has one (see the first pass above). CreateCombinedGoalsValidator guarantees
        // strictly-earlier indices, but that's a separate class — re-check here defensively rather than
        // trust an invariant enforced elsewhere.
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
        }

        db.Goals.AddRange(goals);
        foreach (var project in targetProjects)
        {
            var basePriority = await projects.GetNextPriorityAsync(project.Id, ct);
            db.ProjectGoals.AddRange(goals.Select((goal, i) =>
                ProjectGoalPlanningService.CreateMembership(project, goal, basePriority + i, now)));
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (GoalConflictDetection.IsProjectSlotConflict(ex))
        {
            // The pre-check above already covers the common case; this is the concurrency backstop for a
            // conflicting goal created by a racing request between that check and this save.
            AddError(request => request.Goals, "An active or paused goal of this type already exists for this unit.");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        await planning.NormalizeAsync(targetProjects.Select(project => project.Id), ct);
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);

        var projectIds = targetProjects.Select(project => project.Id.Value).ToList();
        await Send.OkAsync(
            new CreateCombinedGoalsResponse(goals.Select(goal => Map.ToDetail(goal, projectIds)).ToList()), ct);
    }
}

public sealed record CreateCombinedGoalsRequest(
    string EntityType,
    string EntityId,
    List<ProjectPriorityRequest>? Projects,
    List<CombinedGoalSpec> Goals
);

/// <summary>One goal within a combined-creation request. <see cref="DependsOnIndex"/> holds indices into
/// the parent request's <see cref="CreateCombinedGoalsRequest.Goals"/> list — each must reference a
/// strictly earlier position (validated by <see cref="CreateCombinedGoalsValidator"/>), resolved into real
/// <see cref="Goal.DependsOn"/> ids by the endpoint.</summary>
public sealed record CombinedGoalSpec(
    string GoalType,
    CreateGoalConfigRequest Config,
    List<int> DependsOnIndex,
    CreateGoalSnapshotRequest? Snapshot = null
);

public sealed record CreateCombinedGoalsResponse(List<GoalDetailResponse> Goals);
