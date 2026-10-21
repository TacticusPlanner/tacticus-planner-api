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
/// What a transaction-scoped goal edit decided. The edit cores below never write an HTTP response:
/// <see cref="EditGoalEndpoint"/> maps these to the unified 400/409 problems.
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
    public sealed record StaleRevision(Goal Current, List<Guid> ProjectIds) : GoalEditResult;

    public sealed record SlotConflict(GoalSlotConflict Conflict) : GoalEditResult;

    public sealed record OrderConflict(GoalOrderResult Order) : GoalEditResult;

    public sealed record NotFound : GoalEditResult;
}

internal static class GoalEditSaver
{
    /// <summary>The single <c>SaveChangesAsync</c> of an edit, with the shared failure mapping: a slot index
    /// violation becomes <see cref="GoalEditResult.SlotConflict"/> and a concurrent goal revision bump
    /// <see cref="GoalEditResult.StaleRevision"/> (the transaction is rolled back first). Does not commit.</summary>
    public static async Task<GoalEditResult> SaveAsync(
        PlannerDbContext db,
        ProjectGoalPlanningService planning,
        IDbContextTransaction transaction,
        Goal goal,
        CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return new GoalEditResult.Applied();
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            await transaction.DisposeAsync();
            db.ChangeTracker.Clear();
            var current = await db.Goals.AsNoTracking().FirstOrDefaultAsync(entity => entity.Id == goal.Id, ct);
            return current is null
                ? new GoalEditResult.NotFound()
                : new GoalEditResult.StaleRevision(current, await db.ProjectIdsAsync(current.Id, ct));
        }
        catch (DbUpdateException ex) when (ProjectGoalPlanningService.IsSlotViolation(ex))
        {
            return new GoalEditResult.SlotConflict(
                await planning.FindConflictAfterFailedSaveAsync(transaction, [goal], ex, ct));
        }
    }
}

/// <summary>The target section of <see cref="EditGoalEndpoint"/>. Runs under the profile lock on an already
/// re-read goal; mutates the tracked goal and leaves saving to the caller.</summary>
public sealed class GoalTargetEditor(
    PlannerDbContext db, ProjectGoalPlanningService planning, GoalTargetValidationService validation)
{
    public async Task<GoalEditResult> ApplyAsync(
        ProfileId profileId,
        Goal goal,
        List<Guid> projectIds,
        GoalTargetEditRequest target,
        long expectedRevision,
        CancellationToken ct)
    {
        // The status may have changed under the lock (e.g. completed by another request).
        if (RejectionFor(goal, target) is { } rejection)
            return new GoalEditResult.Invalid("Target", rejection);

        // A retry of an edit that already landed carries the old revision but an identical target —
        // answer it with the current goal instead of a spurious stale-revision conflict.
        if (SameTarget(goal, target))
            return new GoalEditResult.Applied(Changed: false);

        if (goal.Revision != expectedRevision)
            return new GoalEditResult.StaleRevision(goal, projectIds);

        if (await ValidateAsync(profileId, goal, target, ct) is { } validationError)
            return new GoalEditResult.Invalid("Target", validationError);

        var previousKey = goal.RankTargetKey;
        ApplyTarget(goal, target);
        goal.RefreshRankTargetKey();

        // Rank occupancy: the new target must be free among the profile's in-flight goals.
        if (goal.RankTargetKey != previousKey && await planning.FindConflictAsync([goal], ct) is { } conflict)
            return new GoalEditResult.SlotConflict(conflict);

        // The target lives in JSON-owned columns, so changing only it leaves the Goal row itself
        // unmodified in the change tracker and EntityMetadataInterceptor would skip the revision bump
        // and UpdatedAt. Touch the row explicitly so this edit is a real revision.
        db.Entry(goal).Property(entity => entity.UpdatedAt).IsModified = true;
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

    /// <summary>Whether <paramref name="target"/> is the goal's current end target (upgrade lists compared as
    /// sets, ranges by value).</summary>
    private static bool SameTarget(Goal goal, GoalTargetEditRequest target)
    {
        var config = goal.Config;
        return goal.GoalType switch
        {
            GoalType.Rank => config.Rank is { } rank
                && rank.End == target.Rank!.End
                && rank.EndPointFive == target.Rank.EndPointFive
                && rank.EndAppliedUpgrades == target.Rank.EndAppliedUpgrades,
            GoalType.Ascension => config.Progression?.End == target.Progression!.End,
            GoalType.Ability => config.Ability is { } ability
                && ability.ActiveEnd == target.Ability!.ActiveEnd
                && ability.PassiveEnd == target.Ability.PassiveEnd,
            GoalType.Upgrade => config.Upgrade is { } upgrade
                && UpgradeKey(upgrade.Targets) == UpgradeKey(UpgradeTargets(target.Upgrade!))
                && SameRange(upgrade.RankRange, GoalMapper.ToRange(target.Upgrade!.RankRange))
                && SameRange(upgrade.ActiveRange, GoalMapper.ToRange(target.Upgrade.ActiveRange))
                && SameRange(upgrade.PassiveRange, GoalMapper.ToRange(target.Upgrade.PassiveRange)),
            _ => false,
        };
    }

    private static List<UpgradeMaterialTarget> UpgradeTargets(UpgradeTargetRequest upgrade) =>
        upgrade.Targets
            .Select(value => new UpgradeMaterialTarget { UpgradeId = value.UpgradeId.Trim(), Quantity = value.Quantity })
            .ToList();

    private static string UpgradeKey(List<UpgradeMaterialTarget> targets) =>
        string.Join('|', targets.OrderBy(target => target.UpgradeId, StringComparer.Ordinal)
            .Select(target => $"{target.UpgradeId}={target.Quantity}"));

    private static bool SameRange(UpgradeRange? a, UpgradeRange? b) =>
        a is null ? b is null : b is not null && a.Start == b.Start && a.End == b.End;

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
                config.Upgrade!.Targets = UpgradeTargets(target.Upgrade!);
                // Replaced as a whole: an omitted range group clears the stored one.
                config.Upgrade.RankRange = GoalMapper.ToRange(target.Upgrade!.RankRange);
                config.Upgrade.ActiveRange = GoalMapper.ToRange(target.Upgrade.ActiveRange);
                config.Upgrade.PassiveRange = GoalMapper.ToRange(target.Upgrade.PassiveRange);
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

/// <summary>The details section (notes/strategy/farming-location/acquisition-source) of
/// <see cref="EditGoalEndpoint"/>. Mutates the tracked goal only.</summary>
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

/// <summary>The projects section of <see cref="EditGoalEndpoint"/>: replaces the goal's memberships with the
/// requested owned projects. Not slot-checked — in-flight uniqueness is per profile, so membership cannot create
/// a duplicate. Mutates the tracked entities and leaves saving to the caller.</summary>
public sealed class GoalMembershipEditor(PlannerDbContext db)
{
    public void Apply(Goal goal, IReadOnlySet<ProjectId> requestedProjectIds, List<ProjectGoal> memberships, DateTimeOffset now)
    {
        db.ProjectGoals.RemoveRange(memberships.Where(entity => !requestedProjectIds.Contains(entity.ProjectId)));
        foreach (var projectId in requestedProjectIds.Where(id => memberships.All(entity => entity.ProjectId != id)))
            db.ProjectGoals.Add(ProjectGoalPlanningService.CreateMembership(projectId, goal.Id, now));
    }
}
