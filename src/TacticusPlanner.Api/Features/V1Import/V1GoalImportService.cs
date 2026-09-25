using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameDomain;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.V1Import;

/// <summary>
/// Translates V1 goals into V2 goals and creates them itself (rewrite-v1-goal-import) — the split
/// design that returned create-request specs for the caller to submit is gone; every value a goal
/// needs (starting point, whether a target is already reached, the initial-state snapshot) is read
/// from the account's live <see cref="PlayerDataSnapshot"/> here, once, rather than round-tripped
/// through the client. Reuses the same collaborators <c>CreateCombinedGoalsEndpoint</c> uses (target
/// validation, the locked-mutation/normalize helpers, the default-project service, the config mapper) —
/// see design.md's "Do not extract a shared creation service".
/// </summary>
public sealed class V1GoalImportService(
    PlannerDbContext db,
    IGameCatalogProvider catalog,
    GoalTargetValidationService targetValidation,
    ProjectGoalPlanningService planning,
    ProjectsService projects,
    TimeProvider timeProvider)
{
    private static readonly string[] Rarities = ["Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythic"];
    private static readonly string[] Stars =
    [
        "None", "OneStar", "TwoStars", "ThreeStars", "FourStars", "FiveStars", "RedOneStar",
        "RedTwoStars", "RedThreeStars", "RedFourStars", "RedFiveStars", "OneBlueStar",
        "TwoBlueStars", "ThreeBlueStars", "MythicWings",
    ];

    public async Task<V1GoalImportResult> ImportAsync(
        ProfileId profileId, IReadOnlyList<V1Goal> source, bool synthesizePrerequisites, CancellationToken ct)
    {
        // The goals part is refused without player data (v1-goal-import spec): every starting point and
        // every already-reached check needs live data, and defaulting instead (the old behavior) invents
        // starting points silently. See design.md's "Refuse the goals part without player data".
        var playerSnapshot = await db.PlayerDataSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == profileId, ct);
        if (playerSnapshot is null)
        {
            return V1GoalImportResult.CreateRefused();
        }

        var ordered = source.OrderBy(goal => goal.Priority).ToList();
        var sourceIdByIndex = ordered.Select(goal => goal.Id).ToArray();
        var outcomeBySourceIndex = new V1GoalOutcome?[ordered.Count];
        var translated = new List<TranslatedGoal>();

        for (var i = 0; i < ordered.Count; i++)
        {
            var (goal, outcome) = Translate(ordered[i], i, playerSnapshot);
            if (goal is null)
            {
                outcomeBySourceIndex[i] = outcome;
            }
            else
            {
                translated.Add(goal);
            }
        }

        var (collapsedSurvivors, mergedInto) = CollapseDuplicates(translated);

        // Loaded as full tracked-then-detached entities, not an anonymous projection of individual
        // columns: Config is a JSON-owned navigation (OwnsOne().ToJson()), and selecting it directly
        // inside a `new { ... }` projection isn't materialized the same way by every provider.
        var existingRows = await db.Goals
            .AsNoTracking()
            .Where(goal => goal.ProfileId == profileId)
            .ToListAsync(ct);
        var existingByKey = ExistingGoalsByKey(existingRows);

        var creatable = new List<TranslatedGoal>();
        foreach (var candidate in collapsedSurvivors)
        {
            if (existingByKey.TryGetValue(candidate.Key, out var existingGoal))
            {
                outcomeBySourceIndex[candidate.OriginalIndex] = new V1GoalOutcome(
                    "Skipped",
                    "goal_already_exists",
                    "The account already has a goal of this type for this unit.",
                    candidate.Key.EntityType.ToString(),
                    candidate.Key.EntityId,
                    candidate.Key.GoalType.ToString(),
                    existingGoal.Id,
                    sourceIdByIndex[candidate.OriginalIndex]);
            }
            else
            {
                creatable.Add(candidate);
            }
        }

        var extraOutcomes = new List<V1GoalOutcome>();
        if (creatable.Count > 0)
        {
            await CreateGoalsAsync(
                profileId, playerSnapshot, creatable, existingByKey, synthesizePrerequisites,
                sourceIdByIndex, outcomeBySourceIndex, extraOutcomes, ct);
        }

        foreach (var (duplicateIndex, survivorIndex) in mergedInto)
        {
            var survivorOutcome = outcomeBySourceIndex[survivorIndex]
                ?? throw new InvalidOperationException("A merge survivor's outcome must resolve before its duplicates.");
            outcomeBySourceIndex[duplicateIndex] = new V1GoalOutcome(
                "Skipped",
                "duplicate_goal_merged",
                "Another V1 goal of this type for this unit was imported in its place.",
                survivorOutcome.EntityType,
                survivorOutcome.EntityId,
                survivorOutcome.GoalType,
                survivorOutcome.GoalId,
                sourceIdByIndex[duplicateIndex]);
        }

        var outcomes = new List<V1GoalOutcome>(ordered.Count + extraOutcomes.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            outcomes.Add(outcomeBySourceIndex[i]
                ?? throw new InvalidOperationException($"V1 goal at index {i} produced no outcome."));
        }
        outcomes.AddRange(extraOutcomes);

        return new V1GoalImportResult(outcomes, Refused: false);
    }

    private (TranslatedGoal? Goal, V1GoalOutcome? Outcome) Translate(
        V1Goal sourceGoal, int originalIndex, PlayerDataSnapshot playerSnapshot)
    {
        if (sourceGoal.Type is 6 or 7)
        {
            return (null, NotImported(sourceGoal, null, null, "unsupported_goal_type",
                $"V1 goal type {sourceGoal.Type} is not supported."));
        }

        // Type 4 is always a Mow ability goal. Ascension (type 2) is the one other type a Mow can carry
        // (V2 allows Ascension/Ability/Upgrade for either entity type) — V1 has no separate wire type for
        // it, so a Mow Ascension goal is distinguished by carrying UnitId instead of Character, the same
        // convention V1 uses for its "upgrade mow" fields.
        var isMow = sourceGoal.Type == 4 || (sourceGoal.Type == 2 && sourceGoal.UnitId is not null);
        var rawUnitId = sourceGoal.UnitId ?? sourceGoal.Character;
        var entityId = isMow
            ? catalog.Current.Mows.FirstOrDefault(item => Matches(item.Id, item.Name, rawUnitId))?.Id
            : catalog.Current.Characters.FirstOrDefault(item => Matches(item.Id, item.Name, sourceGoal.Character))?.Id;
        var entityType = isMow ? GoalEntityType.Mow : GoalEntityType.Character;

        if (entityId is null)
        {
            return (null, NotImported(sourceGoal, entityType.ToString(), rawUnitId, "unknown_unit",
                "The goal's character or Machine of War is not in the V2 Game Catalog."));
        }

        var playerUnit = ResolvePlayerUnit(new UnitKey(entityType, entityId), playerSnapshot);
        var playerCharacter = playerUnit as PlayerCharacterRecord;

        GoalType goalType;
        CreateGoalConfigRequest config;
        switch (sourceGoal.Type)
        {
            case 1 when sourceGoal.TargetRank.HasValue:
                goalType = GoalType.Rank;
                var currentRank = (int)(playerCharacter?.Rank ?? UnitRank.Stone1);
                var targetRank = sourceGoal.TargetRank.Value - 1;
                if (targetRank <= currentRank)
                {
                    return (null, Skipped(sourceGoal, entityType, entityId, goalType, "target_already_reached",
                        "The target rank is at or below the character's current rank."));
                }
                config = new CreateGoalConfigRequest(Rank: new RankTargetRequest(
                    currentRank, false, 0, targetRank, sourceGoal.RankPoint5 ?? false, sourceGoal.RankAppliedUpgrades ?? 0));
                break;

            case 2:
                var currentProgressionWire = CurrentProgressionWire(playerUnit);
                var endWire = ProgressionWire(sourceGoal.TargetRarity, sourceGoal.TargetStars);
                if (endWire is null)
                {
                    return (null, NotImported(sourceGoal, entityType.ToString(), entityId, "invalid_progression",
                        "The ascension target is not on the V2 progression ladder."));
                }
                if (ProgressionRules.ProgressionIndex(endWire) <= ProgressionRules.ProgressionIndex(currentProgressionWire))
                {
                    return (null, Skipped(sourceGoal, entityType, entityId, GoalType.Ascension, "target_already_reached",
                        "The target progression is at or below the unit's current progression."));
                }
                goalType = GoalType.Ascension;
                config = new CreateGoalConfigRequest(
                    Progression: new ProgressionTargetRequest(currentProgressionWire, endWire),
                    AcquisitionSources: BuildAcquisitionSources(
                        entityType, sourceGoal.ShardFarmType, sourceGoal.CampaignsUsage, sourceGoal.MythicCampaignsUsage));
                break;

            case 3:
                if (playerUnit is not null)
                {
                    return (null, Skipped(sourceGoal, entityType, entityId, GoalType.Unlock, "target_already_reached",
                        "The character is already unlocked."));
                }
                goalType = GoalType.Unlock;
                config = new CreateGoalConfigRequest(
                    AcquisitionSources: BuildAcquisitionSources(entityType, null, sourceGoal.CampaignsUsage, null));
                break;

            case 4 or 5 when sourceGoal.FirstAbilityLevel is not null || sourceGoal.SecondAbilityLevel is not null:
                var activeStart = playerUnit?.Abilities.ElementAtOrDefault(0)?.Level ?? 0;
                var passiveStart = playerUnit?.Abilities.ElementAtOrDefault(1)?.Level ?? 0;
                var activeEnd = sourceGoal.FirstAbilityLevel ?? 0;
                var passiveEnd = sourceGoal.SecondAbilityLevel ?? 0;
                if (activeEnd <= activeStart && passiveEnd <= passiveStart)
                {
                    return (null, Skipped(sourceGoal, entityType, entityId, GoalType.Ability, "target_already_reached",
                        "The ability targets are at or below the unit's current levels."));
                }
                goalType = GoalType.Ability;
                config = new CreateGoalConfigRequest(
                    Ability: new AbilityTargetRequest(activeStart, activeEnd, passiveStart, passiveEnd));
                break;

            default:
                return (null, NotImported(sourceGoal, entityType.ToString(), entityId, "missing_target",
                    "The goal is missing a required target."));
        }

        // An absent dailyRaids field (a V1 record predating it) reads as in-planning, so a missing flag
        // never silently pauses an imported goal.
        return (new TranslatedGoal(
            new GoalKey(entityType, entityId, goalType, config.Rank is { } rank
                ? RankTargetKey.From(rank.End, rank.EndPointFive, rank.EndAppliedUpgrades)
                : null),
            config, originalIndex, sourceGoal.Notes,
            InDailyPlanning: sourceGoal.DailyRaids is not false), null);
    }

    /// <summary>Translates V1's shard-source fields (previously dropped entirely) into acquisition
    /// sources. Onslaught is meaningful only for Character Ascension goals (see
    /// <c>AcquisitionSourceRules.SemanticError</c>) — omitted rather than passed through and rejected for
    /// a Machine of War, so an otherwise-valid goal is never failed over an invalid source kind.</summary>
    private static List<AcquisitionSourceRequest>? BuildAcquisitionSources(
        GoalEntityType entityType, string? shardFarmType, int? campaignsUsage, int? mythicCampaignsUsage)
    {
        var sources = new List<AcquisitionSourceRequest>();
        var wantsOnslaught = entityType == GoalEntityType.Character
            && (string.Equals(shardFarmType, V1ShardFarmType.Onslaught, StringComparison.OrdinalIgnoreCase)
                || string.Equals(shardFarmType, V1ShardFarmType.Both, StringComparison.OrdinalIgnoreCase));
        if (wantsOnslaught)
        {
            sources.Add(new AcquisitionSourceRequest(AcquisitionSourceKinds.Onslaught, []));
        }

        // CampaignsLocationsUsage is a numeric enum (None = 0, BestTime = 1, LeastEnergy = 2); only "did
        // the player farm campaigns at all" matters here — an empty Ids list means unrestricted campaign
        // farming, which is the closest V2 shape to V1's strategy preference (it names no specific node).
        if ((campaignsUsage ?? 0) != 0 || (mythicCampaignsUsage ?? 0) != 0)
        {
            sources.Add(new AcquisitionSourceRequest(AcquisitionSourceKinds.Campaign, []));
        }

        return sources.Count == 0 ? null : sources;
    }

    /// <summary>
    /// Indexes the account's existing goals for the "already imported" check. A Rank goal is filed under its
    /// current end target <em>and</em> every target it has held (the previous/new targets of its
    /// <c>TargetChanged</c> events), so a goal the user later edited in place still matches the V1 source it
    /// was imported from — otherwise re-importing would create a second Rank goal at the original target.
    /// The first goal seen for a key wins, as before.
    /// </summary>
    private static Dictionary<GoalKey, ExistingGoalRef> ExistingGoalsByKey(IEnumerable<Goal> existingRows)
    {
        var byKey = new Dictionary<GoalKey, ExistingGoalRef>();
        foreach (var row in existingRows)
        {
            var reference = new ExistingGoalRef(row.Id.Value, row.Config);
            var keys = new HashSet<string?> { RankTargetKey.For(row.GoalType, row.Config) };
            if (row.GoalType == GoalType.Rank)
            {
                foreach (var target in row.Events.SelectMany(goalEvent => new[] { goalEvent.PreviousTarget, goalEvent.NewTarget }))
                {
                    if (target?.RankEnd is { } end)
                    {
                        keys.Add(RankTargetKey.From(end, target.RankEndPointFive ?? false, target.RankEndAppliedUpgrades ?? 0));
                    }
                }
            }

            foreach (var key in keys)
            {
                byKey.TryAdd(new GoalKey(row.EntityType, row.EntityId, row.GoalType, key), reference);
            }
        }

        return byKey;
    }

    private static (List<TranslatedGoal> Survivors, List<(int DuplicateIndex, int SurvivorIndex)> MergedInto)
        CollapseDuplicates(List<TranslatedGoal> translated)
    {
        var survivors = new List<TranslatedGoal>();
        var mergedInto = new List<(int, int)>();

        foreach (var group in translated.GroupBy(goal => goal.Key))
        {
            // OriginalIndex already reflects ascending V1 priority order.
            var ordered = group.OrderBy(goal => goal.OriginalIndex).ToList();
            if (ordered.Count == 1)
            {
                survivors.Add(ordered[0]);
                continue;
            }

            // The survivor stands in for every duplicate, so it is in daily planning if any of them was —
            // merging must not drop a goal the user had active.
            var inDailyPlanning = ordered.Any(goal => goal.InDailyPlanning);

            TranslatedGoal survivor;
            if (group.Key.GoalType == GoalType.Rank)
            {
                var ranks = ordered.Select(goal => goal.Config.Rank!).ToList();
                // Comparing only the integer Start/End loses ties: two goals can share the same rank but
                // differ in EndPointFive/EndAppliedUpgrades, and the tuple key breaks the tie by the actual
                // further-along endpoint instead of arbitrarily keeping whichever goal appeared first.
                var first = ranks.MinBy(rank => (rank.Start, rank.StartPointFive ? 1 : 0, rank.StartAppliedUpgrades))!;
                var last = ranks.MaxBy(rank => (rank.End, rank.EndPointFive ? 1 : 0, rank.EndAppliedUpgrades))!;
                survivor = ordered[0] with
                {
                    Config = new CreateGoalConfigRequest(Rank: new RankTargetRequest(
                        first.Start, first.StartPointFive, first.StartAppliedUpgrades,
                        last.End, last.EndPointFive, last.EndAppliedUpgrades)),
                    Notes = JoinNotes(ordered),
                    InDailyPlanning = inDailyPlanning,
                };
            }
            else if (group.Key.GoalType == GoalType.Ascension)
            {
                var first = ordered.MinBy(goal => ProgressionRules.ProgressionIndex(goal.Config.Progression!.Start))!;
                var last = ordered.MaxBy(goal => ProgressionRules.ProgressionIndex(goal.Config.Progression!.End))!;
                // Duplicate Ascension goals can carry different valid sources (e.g. one Campaign, one
                // Onslaught) — union them instead of keeping only the first goal's list.
                var acquisitionSources = ordered
                    .SelectMany(goal => goal.Config.AcquisitionSources ?? [])
                    .GroupBy(source => source.Kind)
                    .Select(sourceGroup => sourceGroup.First())
                    .ToList();
                survivor = ordered[0] with
                {
                    Config = new CreateGoalConfigRequest(
                        Progression: new ProgressionTargetRequest(first.Config.Progression!.Start, last.Config.Progression!.End),
                        AcquisitionSources: acquisitionSources.Count == 0 ? null : acquisitionSources),
                    Notes = JoinNotes(ordered),
                    InDailyPlanning = inDailyPlanning,
                };
            }
            else
            {
                // Every other goal type has no clear min-start/max-end merge semantics, so only the
                // highest-priority (lowest index) duplicate survives.
                survivor = ordered[0] with { InDailyPlanning = inDailyPlanning };
            }

            survivors.Add(survivor);
            foreach (var extra in ordered.Skip(1))
            {
                mergedInto.Add((extra.OriginalIndex, survivor.OriginalIndex));
            }
        }

        return (survivors, mergedInto);
    }

    private async Task CreateGoalsAsync(
        ProfileId profileId,
        PlayerDataSnapshot playerSnapshot,
        List<TranslatedGoal> creatable,
        Dictionary<GoalKey, ExistingGoalRef> existingByKey,
        bool synthesizePrerequisites,
        string?[] sourceIdByIndex,
        V1GoalOutcome?[] outcomeBySourceIndex,
        List<V1GoalOutcome> extraOutcomes,
        CancellationToken ct)
    {
        // Grouping by unit is still needed to compute each unit's shared prerequisites (Unlock,
        // Ascension, Level each serve every candidate of their unit that needs them) — it no longer
        // determines final priority order, which is assigned in a separate flat pass below (v1-goal-import:
        // "Imported goals preserve V1 priority order").
        var unitOrder = new List<UnitKey>();
        var byUnit = new Dictionary<UnitKey, List<TranslatedGoal>>();
        foreach (var candidate in creatable)
        {
            var unitKey = new UnitKey(candidate.Key.EntityType, candidate.Key.EntityId);
            if (!byUnit.TryGetValue(unitKey, out var list))
            {
                list = [];
                byUnit[unitKey] = list;
                unitOrder.Add(unitKey);
            }
            list.Add(candidate);
        }

        var project = await projects.EnsureDefaultProjectAsync(profileId, ct);

        await planning.ExecuteLockedMutationAsync([project.Id], async transaction =>
        {
            var now = timeProvider.GetUtcNow();
            var staged = new Dictionary<int, Goal>();
            // Synthesized-prerequisite "Created" outcomes (Unlock/Ascension/Level), like the `staged`
            // source-goal ones above, can't be finalized until the transaction actually commits — a
            // project-slot-conflict rollback below must still be able to turn them into failures instead of
            // reporting a goal that was never persisted.
            var stagedPrerequisites = new List<(Goal Goal, int CandidateCount)>();
            // Every goal this batch creates, tagged with a flat-order sort key (v1-goal-import: "Imported
            // goals preserve V1 priority order") instead of getting its priority assigned inline as it's
            // built. AnchorIndex is the goal's own OriginalIndex for a real imported candidate (Rank,
            // Ability, Unlock, Level, or the unit's own imported Ascension goal — none of these are
            // synthesized, so none are pulled out of their natural V1 position), or the lowest OriginalIndex
            // among a synthesized prerequisite's actual dependents (it has no V1 position of its own — this
            // places it immediately before whichever of its dependents appears earliest). SubOrder breaks a
            // tie at the same AnchorIndex in the same order this unit's prerequisites were always built in:
            // Unlock, synthesized Ascension, Level, then the anchoring candidate itself.
            var pending = new List<(Goal Goal, int AnchorIndex, int SubOrder)>();

            foreach (var unitKey in unitOrder)
            {
                var unitCandidates = byUnit[unitKey];
                var playerUnit = ResolvePlayerUnit(unitKey, playerSnapshot);
                // Each imported goal keeps V1's own dailyRaids choice (goal-lifecycle-status). A synthesized
                // prerequisite serves every candidate of its unit, so it is Active if any of them is —
                // pausing it would block a goal the user had in daily planning.
                var prerequisiteStatus = unitCandidates.Any(candidate => candidate.InDailyPlanning)
                    ? GoalStatus.Active
                    : GoalStatus.Paused;
                var prerequisites = synthesizePrerequisites
                    ? ComputePrerequisites(unitKey, unitCandidates, playerUnit, existingByKey, sourceIdByIndex)
                    : UnitPrerequisites.None;

                if (prerequisites.AscensionShortfall is { } shortfall)
                {
                    extraOutcomes.Add(shortfall);
                }

                Guid? unlockGoalId = null;
                if (prerequisites.NeedsUnlock)
                {
                    // Validated like every other synthesized/candidate goal before persisting (unlike the
                    // synthesized Ascension path below, this one isn't structurally guaranteed valid: Unlock
                    // is Character-only and further requires catalog shard-upgrade data — see
                    // GoalTargetValidationService — neither of which ComputePrerequisites checks).
                    var unlockConfig = new CreateGoalConfigRequest();
                    var unlockError = await targetValidation.ValidateAsync(
                        profileId, unitKey.EntityType, unitKey.EntityId, GoalType.Unlock, unlockConfig, ct);
                    if (unlockError is not null)
                    {
                        extraOutcomes.Add(FailedPrerequisiteOutcome(unitKey, GoalType.Unlock, unlockError));
                    }
                    else
                    {
                        // Every candidate of this unit gets a DependsOn edge to Unlock (below), so its
                        // earliest dependent is simply the unit's first candidate by V1 order.
                        var anchorIndex = unitCandidates.Min(candidate => candidate.OriginalIndex);
                        var unlockGoal = BuildGoal(profileId, unitKey, GoalType.Unlock, unlockConfig, null, prerequisiteStatus, now, playerUnit);
                        db.Goals.Add(unlockGoal);
                        pending.Add((unlockGoal, anchorIndex, 0));
                        unlockGoalId = unlockGoal.Id.Value;
                        stagedPrerequisites.Add((unlockGoal, unitCandidates.Count));
                    }
                }

                Guid? ascensionGoalId = null;
                UnitProgression? ascensionFloor = null;
                var ownAscension = unitCandidates.FirstOrDefault(candidate => candidate.Key.GoalType == GoalType.Ascension);
                if (prerequisites.AscensionTarget is { } targetProgression)
                {
                    // Only Rank/Ability candidates of this unit get a DependsOn edge to a synthesized
                    // Ascension (below) — ComputePrerequisites guarantees at least one exists whenever
                    // AscensionTarget is set.
                    var anchorIndex = unitCandidates
                        .Where(candidate => candidate.Key.GoalType is GoalType.Rank or GoalType.Ability)
                        .Min(candidate => candidate.OriginalIndex);
                    var startWire = CurrentProgressionWire(playerUnit);
                    var endWire = ProgressionRules.ProgressionOrder[(int)targetProgression];
                    var ascensionGoal = BuildGoal(
                        profileId, unitKey, GoalType.Ascension,
                        new CreateGoalConfigRequest(Progression: new ProgressionTargetRequest(startWire, endWire)),
                        null, prerequisiteStatus, now, playerUnit);
                    if (unlockGoalId is { } unlockId) ascensionGoal.DependsOn.Add(unlockId);
                    db.Goals.Add(ascensionGoal);
                    pending.Add((ascensionGoal, anchorIndex, 1));
                    ascensionGoalId = ascensionGoal.Id.Value;
                    ascensionFloor = targetProgression;
                    stagedPrerequisites.Add((ascensionGoal, unitCandidates.Count));
                }
                else if (ownAscension is not null)
                {
                    // Not synthesized — this is the unit's own imported Ascension goal, so it keeps its
                    // own natural V1 position (AnchorIndex = its own OriginalIndex) rather than being
                    // pulled early. Its dependency edges and target are still computed and staged here so
                    // Rank/Ability siblings below can depend on it and use its target as their cap floor,
                    // regardless of which of them ends up positioned before or after it.
                    var dependsOn = new List<Guid>();
                    if (unlockGoalId is { } unlockId) dependsOn.Add(unlockId);
                    var error = await targetValidation.ValidateAsync(
                        profileId, unitKey.EntityType, unitKey.EntityId, GoalType.Ascension, ownAscension.Config, ct);
                    if (error is not null)
                    {
                        outcomeBySourceIndex[ownAscension.OriginalIndex] = FailedOutcome(ownAscension, error, sourceIdByIndex);
                    }
                    else
                    {
                        var goal = BuildGoal(profileId, unitKey, GoalType.Ascension, ownAscension.Config, ownAscension.Notes, StatusOf(ownAscension), now, playerUnit);
                        goal.DependsOn = dependsOn;
                        db.Goals.Add(goal);
                        pending.Add((goal, ownAscension.OriginalIndex, 3));
                        staged[ownAscension.OriginalIndex] = goal;
                        ascensionGoalId = goal.Id.Value;
                        ascensionFloor = (UnitProgression)ProgressionRules.ProgressionIndex(ownAscension.Config.Progression!.End);
                    }
                }

                Guid? levelGoalId = null;
                if (prerequisites.LevelTarget is { } targetLevel)
                {
                    // Only Rank candidates of this unit get a DependsOn edge to a synthesized Level (below)
                    // — ComputePrerequisites guarantees at least one exists whenever LevelTarget is set.
                    var anchorIndex = unitCandidates
                        .Where(candidate => candidate.Key.GoalType == GoalType.Rank)
                        .Min(candidate => candidate.OriginalIndex);
                    // Validated before persisting: RequiredLevelForRankTarget can compute a level above
                    // GoalTargetValidationService's MaxCharacterLevel cap (e.g. Adamantine2 + 5 applied
                    // upgrades = 64 > 60), which ComputePrerequisites doesn't itself clamp.
                    var startLevel = (playerUnit as PlayerCharacterRecord)?.XpLevel ?? 0;
                    var levelConfig = new CreateGoalConfigRequest(Level: new LevelTargetRequest(startLevel, targetLevel));
                    var levelError = await targetValidation.ValidateAsync(
                        profileId, unitKey.EntityType, unitKey.EntityId, GoalType.Level, levelConfig, ct);
                    if (levelError is not null)
                    {
                        extraOutcomes.Add(FailedPrerequisiteOutcome(unitKey, GoalType.Level, levelError));
                    }
                    else
                    {
                        var levelGoal = BuildGoal(profileId, unitKey, GoalType.Level, levelConfig, null, prerequisiteStatus, now, playerUnit);
                        if (unlockGoalId is { } unlockId) levelGoal.DependsOn.Add(unlockId);
                        db.Goals.Add(levelGoal);
                        pending.Add((levelGoal, anchorIndex, 2));
                        levelGoalId = levelGoal.Id.Value;
                        stagedPrerequisites.Add((levelGoal, unitCandidates.Count));
                    }
                }

                // ownAscension (if any) was already staged above, at its own natural V1 position.
                foreach (var candidate in unitCandidates.Where(candidate => candidate != ownAscension))
                {
                    var dependsOn = new List<Guid>();
                    if (unlockGoalId is { } unlockId) dependsOn.Add(unlockId);

                    UnitProgression? effectiveFloor = null;
                    if (candidate.Key.GoalType is GoalType.Rank or GoalType.Ability && ascensionGoalId is { } ascId)
                    {
                        dependsOn.Add(ascId);
                        effectiveFloor = ascensionFloor;
                    }
                    if (candidate.Key.GoalType == GoalType.Rank && levelGoalId is { } lvlId)
                    {
                        dependsOn.Add(lvlId);
                    }

                    // ponytail: ValidateAsync re-reads the account's PlayerDataSnapshot from the database
                    // on every call, so a batch of N candidates issues N redundant reads of data already
                    // loaded once at the top of ImportAsync — bounded by V1's own ~100-goal limit and each
                    // read is a single indexed row, so not a real cost today. Upgrade path if it ever is:
                    // give GoalTargetValidationService.ValidateAsync an optional pre-loaded snapshot
                    // parameter (same shape as the effective-progression-floor parameter it already
                    // takes) so a batch caller can pass the one it already has.
                    var validationError = await targetValidation.ValidateAsync(
                        profileId, candidate.Key.EntityType, candidate.Key.EntityId, candidate.Key.GoalType,
                        candidate.Config, ct, effectiveFloor);
                    if (validationError is not null)
                    {
                        outcomeBySourceIndex[candidate.OriginalIndex] = FailedOutcome(candidate, validationError, sourceIdByIndex);
                        continue;
                    }

                    var goal = BuildGoal(profileId, unitKey, candidate.Key.GoalType, candidate.Config, candidate.Notes, StatusOf(candidate), now, playerUnit);
                    goal.DependsOn = dependsOn;
                    db.Goals.Add(goal);
                    pending.Add((goal, candidate.OriginalIndex, 3));
                    staged[candidate.OriginalIndex] = goal;
                }
            }

            // Flat pass: assign priorities in (AnchorIndex, SubOrder) order — this is where V1's priority
            // sequence (and each synthesized prerequisite's "immediately before its earliest dependent"
            // placement) actually becomes the persisted project-goal order.
            var priority = await projects.GetNextPriorityAsync(project.Id, ct);
            foreach (var (goal, _, _) in pending.OrderBy(entry => entry.AnchorIndex).ThenBy(entry => entry.SubOrder))
            {
                db.ProjectGoals.Add(ProjectGoalPlanningService.CreateMembership(project, goal, priority++, now));
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (GoalConflictDetection.IsProjectSlotConflict(ex))
            {
                // Defensive only: the account-wide dedupe pass above already rules this out for a
                // single-writer import; this only fires on a genuine race with a concurrent mutation
                // against the same project. Roll back and report every still-pending candidate as failed
                // rather than losing the whole batch.
                if (transaction is not null) await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                foreach (var (index, _) in staged)
                {
                    outcomeBySourceIndex[index] ??= new V1GoalOutcome(
                        "Failed", "project_slot_conflict",
                        "Another goal for this unit, type and target was created concurrently.",
                        null, null, null, null, sourceIdByIndex[index]);
                }
                foreach (var (goal, _) in stagedPrerequisites)
                {
                    extraOutcomes.Add(new V1GoalOutcome(
                        "Failed", "project_slot_conflict",
                        "Another goal for this unit and type was created concurrently.",
                        goal.EntityType.ToString(), goal.EntityId, goal.GoalType.ToString(), null, null));
                }
                return;
            }

            await planning.NormalizeAsync([project.Id], ct);
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);

            foreach (var (index, goal) in staged)
            {
                outcomeBySourceIndex[index] = new V1GoalOutcome(
                    "Created",
                    "goal_created",
                    "Imported from V1.",
                    goal.EntityType.ToString(),
                    goal.EntityId,
                    goal.GoalType.ToString(),
                    goal.Id.Value,
                    sourceIdByIndex[index]);
            }
            foreach (var (goal, candidateCount) in stagedPrerequisites)
            {
                extraOutcomes.Add(PrerequisiteOutcome(goal, candidateCount));
            }
        }, ct);
    }

    /// <summary>Computes what, if anything, needs synthesizing for one unit — Unlock, Ascension, and/or
    /// Level, at the minimum target that satisfies every requirement among that unit's own creatable
    /// goals (v1-goal-import: "Missing prerequisites are created automatically by the same rules as
    /// manual creation"). Live player state is checked before consulting existing goals, so a completed
    /// (but historically present) prerequisite goal doesn't block synthesis for an already-met
    /// requirement (design.md: "Check live state before consulting existing goals").</summary>
    private static UnitPrerequisites ComputePrerequisites(
        UnitKey unitKey,
        List<TranslatedGoal> unitCandidates,
        PlayerBaseUnitRecord? playerUnit,
        Dictionary<GoalKey, ExistingGoalRef> existingByKey,
        string?[] sourceIdByIndex)
    {
        // Unlock is only ever a valid goal type for a Character (GoalTargetValidationService) — a Mow
        // missing from the roster (e.g. a type-4 Mow Ability goal) never gets a synthesized Unlock; there
        // is no such prerequisite for a Mow in this domain.
        var needsUnlock = unitKey.EntityType == GoalEntityType.Character
            && playerUnit is null
            && !HasOwnOrExistingGoal(unitKey, GoalType.Unlock, unitCandidates, existingByKey);

        var neededProgressionIndex = -1;
        var neededLevel = -1;
        foreach (var candidate in unitCandidates)
        {
            if (candidate.Key.GoalType == GoalType.Rank)
            {
                var rank = (UnitRank)candidate.Config.Rank!.End;
                neededProgressionIndex = Math.Max(neededProgressionIndex, (int)ProgressionRules.MinimumProgressionForRank(rank));
                neededLevel = Math.Max(neededLevel, ProgressionRules.RequiredLevelForRankTarget(
                    rank, candidate.Config.Rank.EndPointFive, candidate.Config.Rank.EndAppliedUpgrades));
            }
            else if (candidate.Key.GoalType == GoalType.Ability)
            {
                var abilityTarget = Math.Max(candidate.Config.Ability!.ActiveEnd, candidate.Config.Ability.PassiveEnd);
                neededProgressionIndex = Math.Max(
                    neededProgressionIndex, (int)ProgressionRules.MinimumProgressionForAbilityLevel(abilityTarget));
            }
        }

        UnitProgression? ascensionTarget = null;
        V1GoalOutcome? ascensionShortfall = null;
        if (neededProgressionIndex >= 0)
        {
            // A unit absent from the roster starts at the bottom of the ladder once unlocked (matches
            // CurrentProgressionWire's default) — 0, not a "below everything" sentinel, so a target
            // whose own minimum requirement is the ladder's first step is not spuriously treated as
            // needing a synthesized Ascension.
            var liveProgressionIndex = playerUnit is not null ? (int)playerUnit.ProgressionIndex : 0;
            if (neededProgressionIndex > liveProgressionIndex)
            {
                var ownAscension = unitCandidates.FirstOrDefault(candidate => candidate.Key.GoalType == GoalType.Ascension);
                if (ownAscension is not null)
                {
                    var ownEnd = ProgressionRules.ProgressionIndex(ownAscension.Config.Progression!.End);
                    if (ownEnd < neededProgressionIndex)
                    {
                        ascensionShortfall = new V1GoalOutcome(
                            "Skipped",
                            "prerequisite_target_insufficient",
                            "The imported Ascension goal's target does not reach what another imported goal for this unit requires.",
                            unitKey.EntityType.ToString(), unitKey.EntityId, GoalType.Ascension.ToString(), null,
                            sourceIdByIndex[ownAscension.OriginalIndex]);
                    }
                }
                else if (existingByKey.TryGetValue(new GoalKey(unitKey.EntityType, unitKey.EntityId, GoalType.Ascension), out var existingGoal))
                {
                    // The existing goal's own target may already reach (or exceed) what's needed — only
                    // report a shortfall when it genuinely falls short, rather than assuming every existing
                    // Ascension goal is insufficient.
                    var existingProgressionIndex = ProgressionRules.ProgressionIndex(existingGoal.Config.Progression!.End);
                    if (existingProgressionIndex < neededProgressionIndex)
                    {
                        ascensionShortfall = new V1GoalOutcome(
                            "Skipped",
                            "prerequisite_target_insufficient",
                            "An existing Ascension goal for this unit does not reach what an imported goal requires.",
                            unitKey.EntityType.ToString(), unitKey.EntityId, GoalType.Ascension.ToString(), existingGoal.Id, null);
                    }
                }
                else
                {
                    ascensionTarget = (UnitProgression)neededProgressionIndex;
                }
            }
        }

        int? levelTarget = null;
        if (unitKey.EntityType == GoalEntityType.Character && neededLevel >= 0)
        {
            // Same reasoning as the progression sentinel above: a freshly unlocked character's real
            // starting level is never below every reachable rank target's own requirement, so 0 (not a
            // "below everything" sentinel) is the safe absent-unit default.
            var liveLevel = (playerUnit as PlayerCharacterRecord)?.XpLevel ?? 0;
            if (neededLevel > liveLevel && !HasOwnOrExistingGoal(unitKey, GoalType.Level, unitCandidates, existingByKey))
            {
                levelTarget = neededLevel;
            }
        }

        return new UnitPrerequisites(needsUnlock, ascensionTarget, levelTarget, ascensionShortfall);
    }

    private static bool HasOwnOrExistingGoal(
        UnitKey unitKey, GoalType goalType, List<TranslatedGoal> unitCandidates, Dictionary<GoalKey, ExistingGoalRef> existingByKey) =>
        unitCandidates.Any(candidate => candidate.Key.GoalType == goalType)
            || existingByKey.ContainsKey(new GoalKey(unitKey.EntityType, unitKey.EntityId, goalType));

    private static Goal BuildGoal(
        ProfileId profileId, UnitKey unitKey, GoalType goalType, CreateGoalConfigRequest config, string? notes,
        GoalStatus status, DateTimeOffset now, PlayerBaseUnitRecord? playerUnit) => new(unitKey.EntityType, unitKey.EntityId, goalType)
        {
            Id = GoalId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            Status = status,
            Notes = notes,
            Config = GoalMapper.MapConfig(config),
            Snapshot = BuildSnapshot(playerUnit),
            Events = [new GoalEvent { At = now, Type = GoalEventType.Created }],
        };

    /// <summary>The goal's initial-state snapshot, built server-side from the already-loaded player data
    /// (design.md: "The snapshot is built server-side from the player data the translator already
    /// loads") — the same fields <c>buildCreateGoalSnapshot</c> resolves client-side for a manually
    /// created goal. <see cref="GoalSnapshot.InitialRequirement"/>/<see cref="GoalSnapshot.InitialInventoryContribution"/>
    /// stay empty, same as the old client-side import path always passed for them.</summary>
    private static GoalSnapshot BuildSnapshot(PlayerBaseUnitRecord? playerUnit) => new()
    {
        InitialRank = (playerUnit as PlayerCharacterRecord)?.Rank,
        InitialProgression = playerUnit?.ProgressionIndex,
        InitialActiveAbilityLevel = playerUnit?.Abilities.ElementAtOrDefault(0)?.Level,
        InitialPassiveAbilityLevel = playerUnit?.Abilities.ElementAtOrDefault(1)?.Level,
    };

    private static V1GoalOutcome PrerequisiteOutcome(Goal prerequisite, int candidateCount) => new(
        "Created",
        "prerequisite_added",
        $"Automatically added because it is required by {candidateCount} imported goal(s) for this unit.",
        prerequisite.EntityType.ToString(),
        prerequisite.EntityId,
        prerequisite.GoalType.ToString(),
        prerequisite.Id.Value,
        null);

    /// <summary>Reports a synthesized Unlock/Level prerequisite that <see cref="GoalTargetValidationService"/>
    /// rejected (e.g. Unlock for a Mow, or a Level target above the character-level cap) — the prerequisite
    /// is not persisted, and dependents proceed without a dependency edge to it, same as an
    /// <see cref="UnitPrerequisites.AscensionShortfall"/> report.</summary>
    private static V1GoalOutcome FailedPrerequisiteOutcome(UnitKey unitKey, GoalType goalType, string message) => new(
        "Failed",
        "prerequisite_rejected",
        message,
        unitKey.EntityType.ToString(),
        unitKey.EntityId,
        goalType.ToString(),
        null,
        null);

    private static V1GoalOutcome FailedOutcome(TranslatedGoal candidate, string message, string?[] sourceIdByIndex) => new(
        "Failed",
        "target_rejected",
        message,
        candidate.Key.EntityType.ToString(),
        candidate.Key.EntityId,
        candidate.Key.GoalType.ToString(),
        null,
        sourceIdByIndex[candidate.OriginalIndex]);

    private static V1GoalOutcome NotImported(V1Goal sourceGoal, string? entityType, string? entityId, string code, string message) =>
        new("Failed", code, message, entityType, entityId, null, null, sourceGoal.Id);

    private static V1GoalOutcome Skipped(
        V1Goal sourceGoal, GoalEntityType entityType, string entityId, GoalType goalType, string code, string message) =>
        new("Skipped", code, message, entityType.ToString(), entityId, goalType.ToString(), null, sourceGoal.Id);

    private static string? ProgressionWire(int? rarity, int? stars)
    {
        if (rarity is null || stars is null || rarity < 0 || rarity >= Rarities.Length || stars < 0 || stars >= Stars.Length)
        {
            return null;
        }
        var value = $"{Rarities[rarity.Value]}:{Stars[stars.Value]}";
        return Array.IndexOf(ProgressionRules.ProgressionOrder, value) >= 0 ? value : null;
    }

    /// <summary>The unit's live progression as a "Rarity:Stars" key. Defaults to the bottom of the
    /// ladder when the unit has no roster record — safe here only because every caller has already
    /// confirmed player data exists for the account overall; an absent unit-level record still means
    /// "not unlocked", not "unknown".</summary>
    private static string CurrentProgressionWire(PlayerBaseUnitRecord? playerUnit)
    {
        var index = playerUnit is null ? 0 : (int)playerUnit.ProgressionIndex;
        return index >= 0 && index < ProgressionRules.ProgressionOrder.Length
            ? ProgressionRules.ProgressionOrder[index]
            : ProgressionRules.ProgressionOrder[0];
    }

    private static PlayerBaseUnitRecord? ResolvePlayerUnit(UnitKey unitKey, PlayerDataSnapshot playerSnapshot) =>
        unitKey.EntityType == GoalEntityType.Character
            ? playerSnapshot.Characters.FirstOrDefault(item => item.UnitId.Value == unitKey.EntityId)
            : playerSnapshot.Mows.FirstOrDefault(item => item.UnitId.Value == unitKey.EntityId);

    private static bool Matches(string id, string name, string? source) =>
        !string.IsNullOrWhiteSpace(source)
        && (string.Equals(id, source, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, source, StringComparison.OrdinalIgnoreCase));

    private static string? JoinNotes(IEnumerable<TranslatedGoal> goals)
    {
        var notes = goals.Select(goal => goal.Notes?.Trim()).Where(note => !string.IsNullOrWhiteSpace(note));
        var result = string.Join(Environment.NewLine, notes);
        return result.Length == 0 ? null : result;
    }

    /// <summary>V1's per-goal <c>dailyRaids</c> choice, carried through to the V2 lifecycle status: a goal
    /// the user had in daily planning imports Active, one they had excluded imports Paused. The import
    /// never consults the profile's active project (goal-lifecycle-status).</summary>
    private static GoalStatus StatusOf(TranslatedGoal goal) =>
        goal.InDailyPlanning ? GoalStatus.Active : GoalStatus.Paused;

    /// <summary><paramref name="RankTargetKey"/> is set only for a Rank goal, so distinct Rank end targets
    /// for one unit are distinct keys (imported as separate milestones) while an exact duplicate shares a
    /// key (merged) — every other goal type keeps the one-per-unit/type key.</summary>
    private sealed record GoalKey(GoalEntityType EntityType, string EntityId, GoalType GoalType, string? RankTargetKey = null);

    private sealed record ExistingGoalRef(Guid Id, GoalConfig Config);

    private sealed record UnitKey(GoalEntityType EntityType, string EntityId);

    /// <summary><see cref="InDailyPlanning"/> carries V1's per-goal <c>dailyRaids</c> choice through to the
    /// created goal's status: in daily planning becomes <see cref="GoalStatus.Active"/>, out of it becomes
    /// <see cref="GoalStatus.Paused"/>.</summary>
    private sealed record TranslatedGoal(
        GoalKey Key, CreateGoalConfigRequest Config, int OriginalIndex, string? Notes, bool InDailyPlanning);

    private sealed record UnitPrerequisites(
        bool NeedsUnlock, UnitProgression? AscensionTarget, int? LevelTarget, V1GoalOutcome? AscensionShortfall)
    {
        public static readonly UnitPrerequisites None = new(false, null, null, null);
    }
}

/// <summary>One outcome per source V1 goal (v1-goal-import spec), plus one per synthesized prerequisite
/// and one per Ascension-target shortfall report. <see cref="Status"/> is one of "Created", "Skipped", or
/// "Failed"; <see cref="Code"/> is a stable machine-readable discriminator finer than <see cref="Status"/>
/// (e.g. "goal_already_exists", "duplicate_goal_merged", "unsupported_goal_type", "prerequisite_added").
/// <see cref="GoalId"/> is the V2 goal this outcome created or matched, when one applies.
/// <see cref="SourceGoalId"/> is the originating V1 goal's id, when this outcome corresponds to one (null
/// for a synthesized prerequisite, which has no single V1 source).</summary>
public sealed record V1GoalOutcome(
    string Status,
    string Code,
    string Message,
    string? EntityType,
    string? EntityId,
    string? GoalType,
    Guid? GoalId,
    string? SourceGoalId
);

public sealed record V1GoalImportResult(IReadOnlyList<V1GoalOutcome> Outcomes, bool Refused)
{
    public static V1GoalImportResult CreateRefused() => new(
        [
            new V1GoalOutcome(
                "Failed",
                "player_data_required",
                "Player data must be synced before goals can be imported.",
                null, null, null, null, null),
        ],
        true);
}
