using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.GameDomain;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Replaces an in-flight goal's <em>end</em> target in place (<c>edit-goal-targets-in-place</c>): the goal's
/// id, baseline, creation snapshot, status, notes, strategy, memberships and priorities are untouched, the
/// change is checked against an expected revision, and a <see cref="GoalEventType.TargetChanged"/> event
/// records the old and new target. A separate endpoint from <see cref="UpdateGoalEndpoint"/> on purpose:
/// that one's "null clears / null leaves unchanged" field semantics would make an omitted target ambiguous.
/// Everything happens under the same project locks as the other slot mutations, so a Rank target edit and a
/// concurrent membership change can't both claim the same target.
/// </summary>
public sealed class UpdateGoalTargetEndpoint : Endpoint<UpdateGoalTargetRequest, GoalDetailResponse, GoalMapper>
{
    public override void Configure()
    {
        Put("me/goals/{goalId}/target");
        Summary(summary =>
        {
            summary.Summary = "Changes an active or paused goal's end target in place.";
            summary.Description = "Supported for Rank, Ascension, Ability and Upgrade goals; Unlock has no "
                + "adjustable target. The new target is validated against the goal's stored baseline and the "
                + "current catalog (an already-reached target is allowed). Submitting the target the goal "
                + "already has is a no-op that changes neither the revision nor the history.";
            summary.Response<GoalDetailResponse>(StatusCodes.Status200OK, "The updated (or unchanged) goal.");
            summary.Response(StatusCodes.Status400BadRequest,
                "The target is malformed, invalid for this goal, or the goal is Unlock/Completed/Archived.");
            summary.Response<GoalRevisionConflictResponse>(StatusCodes.Status409Conflict,
                "expectedRevision is stale (issueCode goalRevisionStale, carrying the current goal), or a "
                + "Rank target is already held in another project (a ProjectGoalSlotConflictResponse).");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No matching goal owned by the caller.");
        });
    }

    public override async Task HandleAsync(UpdateGoalTargetRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var goalId = GoalId.From(Route<Guid>("goalId"));
        var db = Resolve<PlannerDbContext>();
        var planning = Resolve<ProjectGoalPlanningService>();

        // Scoped to the caller's profile by PlannerDbContext's global query filter.
        var goal = await db.Goals.FirstOrDefaultAsync(entity => entity.Id == goalId, ct);
        if (goal is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (RejectionFor(goal, req.Target) is { } rejection)
        {
            AddError(request => request.Target, rejection);
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var lockedProjectIds = await db.ProjectGoals
            .Where(entry => entry.GoalId == goal.Id)
            .Select(entry => entry.ProjectId)
            .ToListAsync(ct);

        // Same restart-on-membership-drift loop as UpdateGoalStatusEndpoint: every project this decision
        // touches must be locked before it is read (ProjectGoalPlanningService's isolation-level invariant).
        while (true)
        {
            List<ProjectId>? restartWithProjectIds = null;
            await planning.ExecuteLockedMutationAsync(lockedProjectIds, async transaction =>
            {
                // Loaded before the lock; a status/target/membership change that committed while this request
                // waited would otherwise be invisible.
                // A full re-read, not ReloadAsync: `before`, the no-op check and the stale-409 body all read
                // Config/Events, which ReloadAsync leaves at their pre-lock values (GoalQueries.ReloadGoalAsync).
                var reloaded = await db.ReloadGoalAsync(goal, ct);
                if (reloaded is null)
                {
                    await Send.NotFoundAsync(ct);
                    return;
                }

                goal = reloaded;
                var membershipProjectIds = await db.ProjectGoals
                    .Where(entry => entry.GoalId == goal.Id)
                    .Select(entry => entry.ProjectId)
                    .ToListAsync(ct);
                if (membershipProjectIds.Except(lockedProjectIds).Any())
                {
                    restartWithProjectIds = membershipProjectIds.Union(lockedProjectIds).ToList();
                    return;
                }

                // The status may have changed under the lock (e.g. completed by another request).
                if (RejectionFor(goal, req.Target) is { } lockedRejection)
                {
                    AddError(request => request.Target, lockedRejection);
                    await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                    return;
                }

                var before = GoalTargetSnapshot.From(goal.GoalType, goal.Config);
                var after = ToSnapshot(goal.GoalType, req.Target);

                // A retry of an edit that already landed carries the old revision but an identical target —
                // answer it with the current goal instead of a spurious stale-revision conflict.
                if (before.SameTargetAs(after))
                {
                    await Send.OkAsync(Map.ToDetail(goal, membershipProjectIds.Select(id => id.Value).ToList()), ct);
                    return;
                }

                if (goal.Revision != req.ExpectedRevision)
                {
                    await SendStaleAsync(goal, membershipProjectIds, ct);
                    return;
                }

                if (await ValidateAsync(profileId, goal, req.Target, ct) is { } validationError)
                {
                    AddError(request => request.Target, validationError);
                    await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                    return;
                }

                var previousKey = RankTargetKey.For(goal.GoalType, goal.Config);
                ApplyTarget(goal, req.Target);
                var newKey = RankTargetKey.For(goal.GoalType, goal.Config);

                // Rank occupancy: the new target must be free in every project this goal belongs to.
                if (newKey != previousKey
                    && await planning.FindConflictAsync(
                        membershipProjectIds, goal.EntityType, goal.EntityId, goal.GoalType, newKey, goal.Id, ct)
                    is { } conflict)
                {
                    HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                    await HttpContext.Response.WriteAsJsonAsync(conflict, ct);
                    return;
                }

                foreach (var membership in await db.ProjectGoals.Where(entry => entry.GoalId == goal.Id).ToListAsync(ct))
                    membership.RankTargetKey = newKey;

                // The target lives in JSON-owned columns, so changing only it leaves the Goal row itself
                // unmodified in the change tracker and EntityMetadataInterceptor would skip the revision bump
                // and UpdatedAt. Touch the row explicitly so this edit is a real revision.
                db.Entry(goal).Property(entity => entity.UpdatedAt).IsModified = true;

                goal.Events.Add(new GoalEvent
                {
                    At = DateTimeOffset.UtcNow,
                    Type = GoalEventType.TargetChanged,
                    PreviousTarget = before,
                    NewTarget = after,
                });

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Something outside the project locks (e.g. a notes edit) bumped the revision after we read it.
                    if (transaction is not null)
                    {
                        await transaction.RollbackAsync(ct);
                        await transaction.DisposeAsync();
                    }

                    db.ChangeTracker.Clear();
                    var current = await db.Goals.AsNoTracking().FirstOrDefaultAsync(entity => entity.Id == goalId, ct);
                    if (current is null)
                    {
                        await Send.NotFoundAsync(ct);
                        return;
                    }

                    await SendStaleAsync(current, membershipProjectIds, ct);
                    return;
                }
                catch (DbUpdateException ex) when (GoalConflictDetection.IsProjectSlotConflict(ex))
                {
                    var databaseConflict = await planning.FindConflictAfterFailedSaveAsync(
                        transaction,
                        [new ProjectGoalSlotLookup(
                            membershipProjectIds, goal.EntityType, goal.EntityId, goal.GoalType, newKey, goal.Id)],
                        ct) ?? throw new InvalidOperationException(
                            "The project slot constraint failed but no conflicting membership was found.", ex);
                    HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                    await HttpContext.Response.WriteAsJsonAsync(databaseConflict, ct);
                    return;
                }

                if (transaction is not null)
                    await transaction.CommitAsync(ct);
                await Send.OkAsync(Map.ToDetail(goal, membershipProjectIds.Select(id => id.Value).ToList()), ct);
            }, ct);

            if (restartWithProjectIds is null)
                break;
            lockedProjectIds = restartWithProjectIds;
        }
    }

    private async Task SendStaleAsync(Goal current, List<ProjectId> projectIds, CancellationToken ct)
    {
        HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        await HttpContext.Response.WriteAsJsonAsync(
            new GoalRevisionConflictResponse(
                "goalRevisionStale",
                "The goal changed since it was loaded. Review the current target and try again.",
                Map.ToDetail(current, projectIds.Select(id => id.Value).ToList())),
            ct);
    }

    /// <summary>Why this goal's target can't be edited at all, or that the payload doesn't carry exactly the
    /// target group of the goal's own kind; null when the shape is fine.</summary>
    private static string? RejectionFor(Goal goal, GoalTargetEditRequest target)
    {
        if (goal.Status is not (GoalStatus.Active or GoalStatus.Paused))
            return "Only an active or paused goal's target can be edited.";
        if (goal.GoalType == GoalType.Unlock)
            return "An Unlock goal has no adjustable target.";

        var present = new[]
        {
            (GoalType.Rank, target.Rank is not null),
            (GoalType.Ascension, target.Progression is not null),
            (GoalType.Ability, target.Ability is not null),
            (GoalType.Upgrade, target.Upgrade is not null),
        }.Where(group => group.Item2).Select(group => group.Item1).ToList();

        return present is [var only] && only == goal.GoalType
            ? null
            : $"The target must contain only the {goal.GoalType} target group.";
    }

    private static GoalTargetSnapshot ToSnapshot(GoalType goalType, GoalTargetEditRequest target) => goalType switch
    {
        GoalType.Rank => new()
        {
            RankEnd = target.Rank!.End,
            RankEndPointFive = target.Rank.EndPointFive,
            RankEndAppliedUpgrades = target.Rank.EndAppliedUpgrades,
        },
        GoalType.Ascension => new() { ProgressionEnd = target.Progression!.End },
        GoalType.Ability => new()
        {
            ActiveAbilityEnd = target.Ability!.ActiveEnd,
            PassiveAbilityEnd = target.Ability.PassiveEnd,
        },
        GoalType.Upgrade => new()
        {
            UpgradeTargets = target.Upgrade!.Targets
                .Select(value => new UpgradeMaterialTarget { UpgradeId = value.UpgradeId.Trim(), Quantity = value.Quantity })
                .ToList(),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(goalType), goalType, "Not an adjustable goal type."),
    };

    /// <summary>Overwrites only the end fields of the goal's own target group; start/baseline, strategy,
    /// sources and location overrides stay as they were.</summary>
    private static void ApplyTarget(Goal goal, GoalTargetEditRequest target)
    {
        var config = goal.Config;
        switch (goal.GoalType)
        {
            case GoalType.Rank:
                config.Rank!.End = target.Rank!.End;
                config.Rank.EndPointFive = target.Rank.EndPointFive;
                config.Rank.EndAppliedUpgrades = target.Rank.EndAppliedUpgrades;
                break;
            case GoalType.Ascension:
                config.Progression!.End = target.Progression!.End;
                break;
            case GoalType.Ability:
                config.Ability!.ActiveEnd = target.Ability!.ActiveEnd;
                config.Ability.PassiveEnd = target.Ability.PassiveEnd;
                break;
            case GoalType.Upgrade:
                config.Upgrade!.Targets = ToSnapshot(goal.GoalType, target).UpgradeTargets!;
                break;
        }
    }

    /// <summary>Creation-equivalent validation of the edited target, built from the goal's stored baseline
    /// plus the new end values and checked against the baseline rather than live player progress.</summary>
    private async Task<string?> ValidateAsync(
        ProfileId profileId, Goal goal, GoalTargetEditRequest target, CancellationToken ct)
    {
        var config = goal.Config;
        var strategy = config.FarmingStrategy.ToString();
        var request = goal.GoalType switch
        {
            GoalType.Rank => new CreateGoalConfigRequest(
                Rank: new RankTargetRequest(
                    config.Rank!.Start, config.Rank.StartPointFive, config.Rank.StartAppliedUpgrades,
                    target.Rank!.End, target.Rank.EndPointFive, target.Rank.EndAppliedUpgrades),
                FarmingStrategy: strategy),
            GoalType.Ascension => new CreateGoalConfigRequest(
                Progression: new ProgressionTargetRequest(config.Progression!.Start, target.Progression!.End),
                AcquisitionSources: null),
            GoalType.Ability => new CreateGoalConfigRequest(
                Ability: new AbilityTargetRequest(
                    config.Ability!.ActiveStart, target.Ability!.ActiveEnd,
                    config.Ability.PassiveStart, target.Ability.PassiveEnd),
                FarmingStrategy: strategy),
            _ => new CreateGoalConfigRequest(Upgrade: target.Upgrade),
        };

        // The rarity cap on Ability targets honors the unit's own Ascension prerequisites, like combined
        // creation does: a stored Ascension dependency's target counts as reachable progression.
        UnitProgression? floor = null;
        if (goal.DependsOn.Count > 0)
        {
            var dependencyIds = goal.DependsOn.Select(GoalId.From).ToList();
            var ends = (await Resolve<PlannerDbContext>().Goals.AsNoTracking()
                    .Where(entity => dependencyIds.Contains(entity.Id) && entity.GoalType == GoalType.Ascension)
                    .ToListAsync(ct))
                .Select(entity => entity.Config.Progression)
                .OfType<ProgressionTarget>()
                .Select(progression => ProgressionRules.ProgressionIndex(progression.End))
                .Where(index => index >= 0)
                .ToList();
            if (ends.Count > 0)
                floor = (UnitProgression)ends.Max();
        }

        return await Resolve<GoalTargetValidationService>().ValidateAsync(
            profileId, goal.EntityType, goal.EntityId, goal.GoalType, request, ct, floor, againstBaseline: true);
    }
}

/// <param name="ExpectedRevision">The <c>revision</c> of the goal detail the editor started from.</param>
public sealed record UpdateGoalTargetRequest(long ExpectedRevision, GoalTargetEditRequest Target);

/// <summary>Exactly the group matching the goal's kind must be set; every other group must be null.
/// Only end values are editable — the start/baseline stays as stored.</summary>
public sealed record GoalTargetEditRequest(
    RankEndTargetRequest? Rank = null,
    ProgressionEndTargetRequest? Progression = null,
    AbilityEndTargetRequest? Ability = null,
    UpgradeTargetRequest? Upgrade = null
);

public sealed record RankEndTargetRequest(int End, bool EndPointFive, int EndAppliedUpgrades);

public sealed record ProgressionEndTargetRequest(string End);

public sealed record AbilityEndTargetRequest(int ActiveEnd, int PassiveEnd);

/// <summary>409 body for a stale <c>expectedRevision</c>: the current goal, so the client can show what
/// changed without another round trip. (A Rank target collision returns a
/// <see cref="ProjectGoalSlotConflictResponse"/> instead — distinguish by <c>issueCode</c>.)</summary>
public sealed record GoalRevisionConflictResponse(string IssueCode, string Message, GoalDetailResponse Goal);

public sealed class UpdateGoalTargetValidator : Validator<UpdateGoalTargetRequest>
{
    public UpdateGoalTargetValidator()
    {
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Target).NotNull();

        // Creation returns a 400 for a missing upgrade list / blank ids; the edit must too, before the
        // handler dereferences them.
        When(request => request.Target?.Upgrade is not null, () =>
        {
            RuleFor(request => request.Target.Upgrade!.Targets)
                .NotNull()
                .WithMessage("An upgrade target needs a list of materials.");
            RuleForEach(request => request.Target.Upgrade!.Targets)
                .Must(target => target is not null && !string.IsNullOrWhiteSpace(target.UpgradeId))
                .When(request => request.Target.Upgrade!.Targets is not null)
                .WithMessage("Every upgrade target needs an upgrade id.");
        });

        When(request => request.Target?.Progression is not null, () =>
            RuleFor(request => request.Target.Progression!.End)
                .NotEmpty()
                .WithMessage("An Ascension target needs a progression step."));
    }
}
