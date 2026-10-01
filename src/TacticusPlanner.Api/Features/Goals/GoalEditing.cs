using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.GameDomain;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// What a transaction-scoped goal edit decided. The edit cores below never write an HTTP response: the
/// thin single-purpose endpoints and <see cref="EditGoalEndpoint"/> map these to the same 400/409 bodies.
/// </summary>
public abstract record GoalEditResult
{
    private GoalEditResult() { }

    /// <summary>The mutation was applied to the tracked entities (not yet saved); <paramref name="Changed"/>
    /// is false for a no-op such as an unchanged target.</summary>
    public sealed record Applied(bool Changed = true) : GoalEditResult;

    /// <summary>A validation failure; <paramref name="Field"/> is the request property it belongs to.</summary>
    public sealed record Invalid(string Field, string Message) : GoalEditResult;

    /// <summary>The goal changed since the client loaded it; <paramref name="Current"/> is the goal as it stands.</summary>
    public sealed record StaleRevision(Goal Current, List<ProjectId> ProjectIds) : GoalEditResult;

    public sealed record SlotConflict(ProjectGoalSlotConflictResponse Body) : GoalEditResult;

    public sealed record OrderConflict(GoalOrderResult Order) : GoalEditResult;

    public sealed record NotFound : GoalEditResult;
}

internal static class GoalEditSaver
{
    /// <summary>The single <c>SaveChangesAsync</c> of an edit, with the shared failure mapping: a project-slot
    /// constraint violation becomes <see cref="GoalEditResult.SlotConflict"/> (the transaction is rolled back
    /// first), and, when <paramref name="staleProjectIds"/> is given, a concurrent goal revision bump becomes
    /// <see cref="GoalEditResult.StaleRevision"/>. Does not commit.</summary>
    public static async Task<GoalEditResult> SaveAsync(
        PlannerDbContext db,
        ProjectGoalPlanningService planning,
        IDbContextTransaction? transaction,
        GoalId goalId,
        ProjectGoalSlotLookup slot,
        List<ProjectId>? staleProjectIds,
        CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return new GoalEditResult.Applied();
        }
        catch (DbUpdateConcurrencyException) when (staleProjectIds is not null)
        {
            // Something outside the project locks (e.g. a notes edit) bumped the revision after we read it.
            if (transaction is not null)
            {
                await transaction.RollbackAsync(ct);
                await transaction.DisposeAsync();
            }

            db.ChangeTracker.Clear();
            var current = await db.Goals.AsNoTracking().FirstOrDefaultAsync(entity => entity.Id == goalId, ct);
            return current is null
                ? new GoalEditResult.NotFound()
                : new GoalEditResult.StaleRevision(current, staleProjectIds);
        }
        catch (DbUpdateException ex) when (GoalConflictDetection.IsProjectSlotConflict(ex))
        {
            var databaseConflict = await planning.FindConflictAfterFailedSaveAsync(transaction, [slot], ct)
                ?? throw new InvalidOperationException(
                    "The project slot constraint failed but no conflicting membership was found.", ex);
            return new GoalEditResult.SlotConflict(databaseConflict);
        }
    }
}

/// <summary>The target-edit core shared by <see cref="UpdateGoalTargetEndpoint"/> and
/// <see cref="EditGoalEndpoint"/>. Runs under the project locks on an already re-read goal; mutates the
/// tracked entities and leaves saving to the caller.</summary>
public sealed class GoalTargetEditor(
    PlannerDbContext db, ProjectGoalPlanningService planning, GoalTargetValidationService validation)
{
    public async Task<GoalEditResult> ApplyAsync(
        ProfileId profileId,
        Goal goal,
        List<ProjectId> membershipProjectIds,
        GoalTargetEditRequest target,
        long expectedRevision,
        CancellationToken ct)
    {
        // The status may have changed under the lock (e.g. completed by another request).
        if (RejectionFor(goal, target) is { } rejection)
            return new GoalEditResult.Invalid("Target", rejection);

        var before = GoalTargetSnapshot.From(goal.GoalType, goal.Config);
        var after = ToSnapshot(goal.GoalType, target);

        // A retry of an edit that already landed carries the old revision but an identical target —
        // answer it with the current goal instead of a spurious stale-revision conflict.
        if (before.SameTargetAs(after))
            return new GoalEditResult.Applied(Changed: false);

        if (goal.Revision != expectedRevision)
            return new GoalEditResult.StaleRevision(goal, membershipProjectIds);

        if (await ValidateAsync(profileId, goal, target, ct) is { } validationError)
            return new GoalEditResult.Invalid("Target", validationError);

        var previousKey = RankTargetKey.For(goal.GoalType, goal.Config);
        ApplyTarget(goal, target);
        var newKey = RankTargetKey.For(goal.GoalType, goal.Config);

        // Rank occupancy: the new target must be free in every project this goal belongs to.
        if (newKey != previousKey
            && await planning.FindConflictAsync(
                membershipProjectIds, goal.EntityType, goal.EntityId, goal.GoalType, newKey, goal.Id, ct)
            is { } conflict)
        {
            return new GoalEditResult.SlotConflict(conflict);
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
        return new GoalEditResult.Applied();
    }

    /// <summary>Why this goal's target can't be edited at all, or that the payload doesn't carry exactly the
    /// target group of the goal's own kind; null when the shape is fine.</summary>
    public static string? RejectionFor(Goal goal, GoalTargetEditRequest target)
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
            UpgradeRankRange = GoalMapper.ToRange(target.Upgrade.RankRange),
            UpgradeActiveRange = GoalMapper.ToRange(target.Upgrade.ActiveRange),
            UpgradePassiveRange = GoalMapper.ToRange(target.Upgrade.PassiveRange),
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
                var edited = ToSnapshot(goal.GoalType, target);
                config.Upgrade!.Targets = edited.UpgradeTargets!;
                // Replaced as a whole: an omitted range group clears the stored one.
                config.Upgrade.RankRange = edited.UpgradeRankRange;
                config.Upgrade.ActiveRange = edited.UpgradeActiveRange;
                config.Upgrade.PassiveRange = edited.UpgradePassiveRange;
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
            var ends = (await db.Goals.AsNoTracking()
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

        return await validation.ValidateAsync(
            profileId, goal.EntityType, goal.EntityId, goal.GoalType, request, ct, floor, againstBaseline: true);
    }
}

/// <summary>The notes/strategy/farming-location/acquisition-source core shared by
/// <see cref="UpdateGoalEndpoint"/> and <see cref="EditGoalEndpoint"/>. Mutates the tracked goal only.</summary>
public sealed class GoalDetailsEditor(GoalTargetValidationService validation)
{
    public GoalEditResult Apply(Goal goal, UpdateGoalRequest req)
    {
        FarmingStrategy? farmingStrategy = null;
        if (req.FarmingStrategy is not null)
        {
            if (!Enum.TryParse<FarmingStrategy>(req.FarmingStrategy, ignoreCase: true, out var parsedStrategy)
                || !Enum.IsDefined(parsedStrategy)
                || int.TryParse(req.FarmingStrategy, out _))
            {
                return new GoalEditResult.Invalid(nameof(UpdateGoalRequest.FarmingStrategy), "Unknown farming strategy.");
            }

            if (parsedStrategy != FarmingStrategy.TotalUpgrades
                && goal.GoalType != GoalType.Rank
                && !(goal.GoalType == GoalType.Ability && goal.EntityType == GoalEntityType.Mow))
            {
                return new GoalEditResult.Invalid(
                    nameof(UpdateGoalRequest.FarmingStrategy),
                    "Farming strategy is supported only for Character Rank and Machine of War Ability goals.");
            }

            farmingStrategy = parsedStrategy;
        }

        var farmingLocationIds = req.FarmingLocationIds?.Select(id => id.Value).ToList();
        if (validation.ValidateFarmingLocationOverride(goal.GoalType, goal.EntityId, farmingLocationIds) is { } farmingError)
            return new GoalEditResult.Invalid(nameof(UpdateGoalRequest.FarmingLocationIds), farmingError);

        if (validation.ValidateAcquisitionSources(
                goal.GoalType, goal.EntityType, goal.EntityId, req.AcquisitionSources) is { } acquisitionError)
        {
            return new GoalEditResult.Invalid(nameof(UpdateGoalRequest.AcquisitionSources), acquisitionError);
        }

        goal.Notes = req.Notes;
        goal.Config.FarmingLocationIds = farmingLocationIds;
        if (req.AcquisitionSources is not null)
        {
            goal.Config.AcquisitionSources = GoalMapper.MapAcquisitionSources(req.AcquisitionSources);
        }

        if (farmingStrategy is not null)
        {
            goal.Config.FarmingStrategy = farmingStrategy.Value;
        }

        return new GoalEditResult.Applied();
    }
}

/// <summary>The project-membership core shared by <see cref="UpdateGoalProjectsEndpoint"/> and
/// <see cref="EditGoalEndpoint"/>. Runs under the project locks against memberships re-read under them;
/// mutates the tracked entities and leaves saving to the caller.</summary>
public sealed class GoalMembershipEditor(PlannerDbContext db, ProjectGoalPlanningService planning)
{
    public async Task<GoalEditResult> ApplyAsync(
        Goal goal,
        HashSet<ProjectId> requestedProjectIds,
        List<Project> ownedProjects,
        List<ProjectGoal> memberships,
        CancellationToken ct)
    {
        if (goal.Status is GoalStatus.Active or GoalStatus.Paused
            && await planning.FindConflictAsync(
                requestedProjectIds, goal.EntityType, goal.EntityId, goal.GoalType,
                RankTargetKey.For(goal.GoalType, goal.Config), goal.Id, ct) is { } conflict)
        {
            return new GoalEditResult.SlotConflict(conflict);
        }

        var toRemove = memberships.Where(entity => !requestedProjectIds.Contains(entity.ProjectId)).ToList();
        if (toRemove.Count > 0)
        {
            db.ProjectGoals.RemoveRange(toRemove);
        }

        var existingByProjectId = memberships.ToDictionary(entity => entity.ProjectId);
        foreach (var project in ownedProjects)
        {
            if (!existingByProjectId.ContainsKey(project.Id))
            {
                db.ProjectGoals.Add(ProjectGoalPlanningService.CreateMembership(project, goal, DateTimeOffset.UtcNow));
            }
        }

        return new GoalEditResult.Applied();
    }
}
