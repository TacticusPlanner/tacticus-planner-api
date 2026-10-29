using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameDomain;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.V1Import;

/// <summary>
/// Translates V1 goals into V2 goals and creates them itself (rewrite-v1-goal-import) — the split
/// design that returned create-request specs for the caller to submit is gone; every value a goal
/// needs (starting point, whether a target is already reached, the initial-state snapshot) is read
/// from the account's live <see cref="PlayerDataSnapshot"/> here, once, rather than round-tripped
/// through the client. Reuses the same collaborators <c>CreateCombinedGoalsEndpoint</c> uses (target
/// validation, the locked-mutation/normalize helpers, the config mapper) —
/// see design.md's "Do not extract a shared creation service".
/// </summary>
public sealed class V1GoalImportService(
    PlannerDbContext db,
    IGameCatalogProvider catalog,
    GoalTargetValidationService targetValidation,
    ProjectGoalPlanningService planning,
    GoalOrderService order,
    TimeProvider timeProvider)
{
    // V1's PersonalGoalType values for its two unit-less material goal types.
    private const int V1UpgradeMaterialType = 6;
    private const int V1PreFarmType = 7;

    /// <summary>The most V1 goals one import reads (V1 itself holds about 100); the rest are reported once.</summary>
    internal const int MaxSourceGoals = 500;

    /// <summary>The most outcomes one import returns; any beyond are summarized in the last one.</summary>
    internal const int MaxOutcomes = 1000;

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

        var ordered = source.OrderBy(goal => goal.Priority).Take(MaxSourceGoals).ToList();
        var sourceIdByIndex = ordered.Select(goal => goal.Id).ToArray();
        // One outcome slot per source goal, plus one more for each further Upgrade goal a pre-farm goal
        // fans out into (one per linked unit) — reported right after their source goal's own slot.
        var slotSourceIndex = Enumerable.Range(0, ordered.Count).ToList();
        var outcomeBySlotList = new List<V1GoalOutcome?>(new V1GoalOutcome?[ordered.Count]);
        var translated = new List<TranslatedGoal>();

        // Loaded as full tracked-then-detached entities, not an anonymous projection of individual
        // columns: Config is a JSON-owned navigation (OwnsOne().ToJson()), and selecting it directly
        // inside a `new { ... }` projection isn't materialized the same way by every provider. Loaded
        // before translation because choosing an Upgrade Material goal's owner consults existing goals.
        var existingRows = await db.Goals
            .AsNoTracking()
            .Where(goal => goal.ProfileId == profileId)
            .ToListAsync(ct);
        var existingByKey = ExistingGoalsByKey(existingRows);
        var existingMaterials = ExistingUpgradeMaterials(existingRows);
        var existingAscensions = ExistingAscensionsByUnit(existingRows);

        // Unit goals first: a pre-farm goal (type 7) becomes Upgrade goals on its linked goals' units, and their
        // units, in V1 priority order, are the preferred owners for a material goal (type 6). Both are
        // translated in a second pass below.
        var materialGoalIndexes = new List<int>();
        var preferredOwners = new List<UnitKey>();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Type is V1UpgradeMaterialType or V1PreFarmType)
            {
                materialGoalIndexes.Add(i);
                continue;
            }

            var (goal, outcome) = Translate(ordered[i], i, playerSnapshot);
            if (goal is null)
            {
                outcomeBySlotList[i] = outcome;
            }
            else
            {
                translated.Add(goal);
                preferredOwners.Add(new UnitKey(goal.Key.EntityType, goal.Key.EntityId));
            }
        }

        var owners = new UpgradeOwnerResolver(catalog.Current, playerSnapshot, preferredOwners, existingMaterials);
        var unitGoalsBySourceId = translated
            .Where(goal => sourceIdByIndex[goal.OriginalIndex] is not null)
            .GroupBy(goal => sourceIdByIndex[goal.OriginalIndex]!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var i in materialGoalIndexes)
        {
            var (candidates, outcome) = TranslateMaterialGoal(ordered[i], i, owners, unitGoalsBySourceId, playerSnapshot);
            outcomeBySlotList[i] = outcome;
            for (var sequence = 0; sequence < candidates.Count; sequence++)
            {
                var slot = i;
                if (sequence > 0)
                {
                    slot = slotSourceIndex.Count;
                    slotSourceIndex.Add(i);
                    outcomeBySlotList.Add(null);
                }
                translated.Add(candidates[sequence] with { SlotOverride = slot, Sequence = sequence });
            }
        }

        var outcomeBySourceIndex = outcomeBySlotList.ToArray();
        var sourceIdBySlot = slotSourceIndex.Select(index => sourceIdByIndex[index]).ToArray();
        var (collapsedSurvivors, mergedInto) = CollapseDuplicates(translated);

        var creatable = new List<TranslatedGoal>();
        foreach (var candidate in collapsedSurvivors)
        {
            // A material goal is already there when one of the unit's Upgrade goals targets its material; every
            // other goal when the unit has a goal of its type with the same end target (goal-target-milestones).
            var existingGoalId = candidate.MaterialId is { } materialId
                ? existingMaterials.TryGetValue((new UnitKey(candidate.Key.EntityType, candidate.Key.EntityId), materialId), out var materialGoalId)
                    ? materialGoalId
                    : (Guid?)null
                : existingByKey.GetValueOrDefault(candidate.Key)?.Id;
            if (existingGoalId is { } existingId)
            {
                outcomeBySourceIndex[candidate.Slot] = new V1GoalOutcome(
                    V1GoalOutcomeStatus.Skipped,
                    "goal_already_exists",
                    candidate.MaterialId is not null
                        ? "The account already has an Upgrade goal for this material on this unit."
                        : candidate.Key.TargetKey is null
                            ? "The account already has a goal of this type for this unit."
                            : "The account already has a goal of this type with this target for this unit.",
                    candidate.Key.EntityType,
                    candidate.Key.EntityId,
                    candidate.Key.GoalType,
                    existingId,
                    sourceIdBySlot[candidate.Slot],
                    candidate.MaterialId,
                    candidate.LinkedSourceGoalIds,
                    candidate.MaterialQuantity);
            }
            else
            {
                creatable.Add(candidate);
            }
        }

        var extraOutcomes = new List<V1GoalOutcome>();
        Guid? projectId = null;
        if (creatable.Count > 0)
        {
            projectId = await CreateGoalsAsync(
                profileId, playerSnapshot, creatable, existingByKey, existingAscensions, synthesizePrerequisites,
                sourceIdBySlot, outcomeBySourceIndex, extraOutcomes, ct);
        }

        foreach (var (duplicate, survivor) in mergedInto)
        {
            var survivorOutcome = outcomeBySourceIndex[survivor.Slot]
                ?? throw new InvalidOperationException("A merge survivor's outcome must resolve before its duplicates.");
            outcomeBySourceIndex[duplicate.Slot] = new V1GoalOutcome(
                V1GoalOutcomeStatus.Skipped,
                "duplicate_goal_merged",
                duplicate.Key.GoalType == GoalType.Upgrade
                    ? "Merged into this unit's Upgrade goal for the same material."
                    : "Another V1 goal of this type with the same target for this unit was imported in its place.",
                survivorOutcome.EntityType,
                survivorOutcome.EntityId,
                survivorOutcome.GoalType,
                survivorOutcome.GoalId,
                sourceIdBySlot[duplicate.Slot],
                duplicate.MaterialId,
                duplicate.LinkedSourceGoalIds,
                duplicate.MaterialQuantity);
        }

        // Source order, each pre-farm goal's further per-unit slots right after its own.
        var outcomes = new List<V1GoalOutcome>(outcomeBySourceIndex.Length + extraOutcomes.Count);
        foreach (var slot in Enumerable.Range(0, outcomeBySourceIndex.Length).OrderBy(slot => slotSourceIndex[slot]).ThenBy(slot => slot))
        {
            outcomes.Add(outcomeBySourceIndex[slot]
                ?? throw new InvalidOperationException($"V1 goal at index {slotSourceIndex[slot]} produced no outcome."));
        }
        outcomes.AddRange(extraOutcomes);
        if (source.Count > MaxSourceGoals)
        {
            outcomes.Add(new V1GoalOutcome(
                V1GoalOutcomeStatus.Skipped, "too_many_goals",
                $"Only the first {MaxSourceGoals} V1 goals by priority were imported; {source.Count - MaxSourceGoals} were not.",
                null, null, null, null, null));
        }

        if (outcomes.Count > MaxOutcomes)
        {
            var omitted = outcomes.Count - (MaxOutcomes - 1);
            outcomes = outcomes.Take(MaxOutcomes - 1).ToList();
            outcomes.Add(new V1GoalOutcome(
                V1GoalOutcomeStatus.Skipped, "outcomes_truncated", $"{omitted} more goal outcomes were not listed.",
                null, null, null, null, null));
        }

        return new V1GoalImportResult(outcomes, Refused: false) { ProjectId = projectId };
    }

    private (TranslatedGoal? Goal, V1GoalOutcome? Outcome) Translate(
        V1Goal sourceGoal, int originalIndex, PlayerDataSnapshot playerSnapshot)
    {
        if (sourceGoal.Type is < 1 or > 5)
        {
            return (null, NotImported(sourceGoal, null, null, "unsupported_goal_type",
                $"V1 goal type {sourceGoal.Type} is not supported."));
        }

        // Type 4 is always a Mow ability goal. Ascension (type 2) is the one other type a Mow can carry
        // (V2 allows Ascension/Ability/Upgrade for either entity type) — V1 has no separate wire type for
        // it. V1's own goal dialog stores every unit, Mow or Character, under Character, so a type-2 goal
        // whose Character is not a catalog character is looked up among the Mows; a record carrying
        // UnitId instead (the "upgrade mow" field convention) is a Mow outright.
        var rawUnitId = sourceGoal.UnitId ?? sourceGoal.Character;
        var isMow = sourceGoal.Type == 4 || (sourceGoal.Type == 2 && sourceGoal.UnitId is not null);
        var entityId = isMow
            ? catalog.Current.Mows.FirstOrDefault(item => Matches(item.Id, item.Name, rawUnitId))?.Id
            : catalog.Current.Characters.FirstOrDefault(item => Matches(item.Id, item.Name, sourceGoal.Character))?.Id;
        if (entityId is null && sourceGoal.Type == 2 && !isMow)
        {
            entityId = catalog.Current.Mows.FirstOrDefault(item => Matches(item.Id, item.Name, sourceGoal.Character))?.Id;
            isMow = entityId is not null;
        }
        var entityType = isMow ? GoalEntityType.Mow : GoalEntityType.Character;

        if (entityId is null)
        {
            return (null, NotImported(sourceGoal, entityType, rawUnitId, "unknown_unit",
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
                    return (null, NotImported(sourceGoal, entityType, entityId, "invalid_progression",
                        "The ascension target is not on the V2 progression ladder."));
                }
                if (ProgressionRules.ProgressionIndex(endWire) <= ProgressionRules.ProgressionIndex(currentProgressionWire))
                {
                    return (null, Skipped(sourceGoal, entityType, entityId, GoalType.Ascension, "target_already_reached",
                        "The target progression is at or below the unit's current progression."));
                }
                goalType = GoalType.Ascension;
                config = new CreateGoalConfigRequest(
                    Progression: new ProgressionTargetRequest(
                        ProgressionRules.FromWireName(currentProgressionWire), ProgressionRules.FromWireName(endWire)),
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
                return (null, NotImported(sourceGoal, entityType, entityId, "missing_target",
                    "The goal is missing a required target."));
        }

        // An absent dailyRaids field (a V1 record predating it) reads as in-planning, so a missing flag
        // never silently pauses an imported goal.
        return (new TranslatedGoal(
            new GoalKey(entityType, entityId, goalType, GoalTargetKey.For(goalType, GoalMapper.MapConfig(config))),
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
            sources.Add(new AcquisitionSourceRequest(AcquisitionSourceKind.Onslaught, []));
        }

        // CampaignsLocationsUsage is a numeric enum (None = 0, BestTime = 1, LeastEnergy = 2); only "did
        // the player farm campaigns at all" matters here — an empty Ids list means unrestricted campaign
        // farming, which is the closest V2 shape to V1's strategy preference (it names no specific node).
        if ((campaignsUsage ?? 0) != 0 || (mythicCampaignsUsage ?? 0) != 0)
        {
            sources.Add(new AcquisitionSourceRequest(AcquisitionSourceKind.Campaign, []));
        }

        return sources.Count == 0 ? null : sources;
    }

    /// <summary>V1's two unit-less material goals become Upgrade goals at the V1 goal's own priority:
    /// <list type="bullet">
    /// <item>Type 7 (pre-farm a material for chosen goals) fans out into one Upgrade goal per distinct unit of
    /// its linked goals that were imported, in link order, each for as much of the material as that unit's
    /// linked goals still need (<see cref="MaterialNeed"/>). A unit that no longer needs it is left out.</item>
    /// <item>Type 6 ("farm N of one material") has no unit, so its Upgrade goal goes on the suggested unit
    /// (<see cref="UpgradeOwnerResolver"/>) and is reported as <c>upgrade_goal_assigned</c>, so the user
    /// can move it.</item>
    /// </list>
    /// A pre-farm goal whose linked goals weren't imported falls back to a manual step naming the material
    /// and a suggested unit. A material no candidate unit uses could never become an Upgrade goal, so it
    /// reports as not imported.</summary>
    private (List<TranslatedGoal> Candidates, V1GoalOutcome? Outcome) TranslateMaterialGoal(
        V1Goal sourceGoal,
        int originalIndex,
        UpgradeOwnerResolver owners,
        IReadOnlyDictionary<string, TranslatedGoal> unitGoalsBySourceId,
        PlayerDataSnapshot playerSnapshot)
    {
        var isPreFarm = sourceGoal.Type == V1PreFarmType;
        var upgradeId = sourceGoal.UpgradeMaterialId?.Trim();
        var quantity = sourceGoal.UpgradeMaterialQuantity;
        if (string.IsNullOrEmpty(upgradeId) || (!isPreFarm && quantity is not > 0))
        {
            return (new List<TranslatedGoal>(), NotImported(sourceGoal, null, null, "missing_target",
                "The goal is missing a required target.", upgradeId));
        }

        if (catalog.Current.UpgradeViews.All(item => item.Id != upgradeId))
        {
            return (new List<TranslatedGoal>(), NotImported(sourceGoal, null, null, "unknown_upgrade",
                "The goal's upgrade material is not in the V2 Game Catalog.", upgradeId));
        }

        var inDailyPlanning = sourceGoal.DailyRaids is not false;
        if (!isPreFarm)
        {
            if (owners.Resolve(upgradeId) is not { } owner)
            {
                return (new List<TranslatedGoal>(), NotImported(sourceGoal, null, null, "no_upgrade_owner",
                    "No character or Machine of War on the account uses this upgrade material.", upgradeId));
            }

            return (new List<TranslatedGoal> { UpgradeCandidate(owner, upgradeId, quantity!.Value, originalIndex, sourceGoal.Notes, inDailyPlanning,
                "upgrade_goal_assigned", null) }, null);
        }

        var linkedIds = (sourceGoal.PreFarmGoalIds ?? [])
            .Distinct(StringComparer.Ordinal)
            .Where(unitGoalsBySourceId.ContainsKey)
            .ToList();
        var linkedGoals = linkedIds.Select(id => unitGoalsBySourceId[id]).ToList();
        if (linkedGoals.Count == 0)
        {
            if (owners.Resolve(upgradeId) is not { } suggested)
            {
                return (new List<TranslatedGoal>(), NotImported(sourceGoal, null, null, "no_upgrade_owner",
                    "No character or Machine of War on the account uses this upgrade material.", upgradeId));
            }

            return (new List<TranslatedGoal>(), new V1GoalOutcome(
                V1GoalOutcomeStatus.Skipped,
                "manual_step_required",
                "None of the goals this pre-farm goal was for were imported. Create an Upgrade goal for this material on the unit you were pre-farming for.",
                suggested.EntityType, suggested.EntityId, GoalType.Upgrade, null, sourceGoal.Id,
                upgradeId, linkedIds, null));
        }

        var candidates = linkedGoals
            .GroupBy(goal => new UnitKey(goal.Key.EntityType, goal.Key.EntityId))
            .Where(unitGoals => owners.Uses(unitGoals.Key, upgradeId))
            .Select(unitGoals => (Unit: unitGoals.Key, Need: MaterialNeed(unitGoals.Key, upgradeId, unitGoals.ToList(), playerSnapshot)))
            .Where(entry => entry.Need > 0)
            .Select(entry => UpgradeCandidate(entry.Unit, upgradeId, entry.Need, originalIndex, sourceGoal.Notes, inDailyPlanning,
                null, linkedIds))
            .ToList();

        if (candidates.Count == 0)
        {
            return (new List<TranslatedGoal>(), new V1GoalOutcome(
                V1GoalOutcomeStatus.Skipped,
                "target_already_reached",
                "None of the units this pre-farm goal was for still needs this material.",
                null, null, GoalType.Upgrade, null, sourceGoal.Id,
                upgradeId, linkedIds, null));
        }

        return (candidates, null);
    }

    private static TranslatedGoal UpgradeCandidate(
        UnitKey unit, string upgradeId, int quantity, int originalIndex, string? notes, bool inDailyPlanning,
        string? createdCode, IReadOnlyList<string>? linkedIds) =>
        new(
            // Grouped per (unit, material) so the same material merges while different materials stay
            // separate Upgrade goals; the persisted goal's own target key is checked under the lock.
            new GoalKey(unit.EntityType, unit.EntityId, GoalType.Upgrade, "material:" + upgradeId),
            new CreateGoalConfigRequest(Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest(upgradeId, quantity)])),
            originalIndex, notes, inDailyPlanning)
        {
            CreatedCode = createdCode,
            MaterialId = upgradeId,
            MaterialQuantity = quantity,
            LinkedSourceGoalIds = linkedIds,
        };

    /// <summary>How many of <paramref name="upgradeId"/> the unit still needs for its linked goals, counting
    /// every crafted upgrade down through its recipe: a Character from its live rank up to its highest linked
    /// Rank target (the whole remaining ladder when none is a Rank goal); a Mow from its live ability levels up
    /// to its linked Ability targets (both full tracks when none is an Ability goal). Upgrades already applied at
    /// the current rank are not netted out, nor is inventory — the goal's own estimate does that.</summary>
    private int MaterialNeed(
        UnitKey unit, string upgradeId, IReadOnlyList<TranslatedGoal> unitGoals, PlayerDataSnapshot playerSnapshot)
    {
        var playerUnit = ResolvePlayerUnit(unit, playerSnapshot);
        var ids = new List<string>();
        if (unit.EntityType == GoalEntityType.Character)
        {
            var steps = catalog.Current.CharacterViews.FirstOrDefault(item => item.Id == unit.EntityId)?.RankUpUpgrades;
            if (steps is null) return 0;
            var start = (int)((playerUnit as PlayerCharacterRecord)?.Rank ?? UnitRank.Stone1);
            var target = unitGoals
                .Select(goal => goal.Config.Rank)
                .OfType<RankTargetRequest>()
                .MaxBy(rank => (rank.End, rank.EndPointFive ? 1 : 0, rank.EndAppliedUpgrades));
            var end = target?.End ?? steps.Count;
            ids.AddRange(steps.Skip(start).Take(Math.Max(0, end - start)).SelectMany(step => step.UpgradeIds));
            if (target is not null && steps.ElementAtOrDefault(end) is { } endStep)
            {
                // Upgrades are column-major (two rows of three), so a ".5" target is the top row: 0, 2 and 4.
                ids.AddRange(target.EndAppliedUpgrades > 0
                    ? endStep.UpgradeIds.Take(target.EndAppliedUpgrades)
                    : target.EndPointFive ? endStep.UpgradeIds.Where((_, index) => index % 2 == 0) : Enumerable.Empty<string>());
            }
        }
        else
        {
            var mow = catalog.Current.MowList.FirstOrDefault(item => item.Id == unit.EntityId);
            if (mow is null) return 0;
            var abilityTargets = unitGoals.Select(goal => goal.Config.Ability).OfType<AbilityTargetRequest>().ToList();
            var activeEnd = abilityTargets.Count > 0 ? abilityTargets.Max(ability => ability.ActiveEnd) : mow.PrimaryAbility.Recipes.Count + 1;
            var passiveEnd = abilityTargets.Count > 0 ? abilityTargets.Max(ability => ability.PassiveEnd) : mow.SecondaryAbility.Recipes.Count + 1;
            ids.AddRange(RecipeRange(mow.PrimaryAbility.Recipes, playerUnit?.Abilities.ElementAtOrDefault(0)?.Level ?? 0, activeEnd));
            ids.AddRange(RecipeRange(mow.SecondaryAbility.Recipes, playerUnit?.Abilities.ElementAtOrDefault(1)?.Level ?? 0, passiveEnd));
        }

        var upgrades = catalog.Current.UpgradeViews.ToDictionary(item => item.Id, StringComparer.Ordinal);
        return ids.Sum(id => CountWithin(id, upgradeId, upgrades, 0));
    }

    /// <summary>A Mow ability track's recipes for levels <paramref name="start"/> up to <paramref name="end"/>:
    /// recipe <c>n</c> takes level <c>n + 1</c> to <c>n + 2</c>.</summary>
    private static IEnumerable<string> RecipeRange(IReadOnlyList<IReadOnlyList<string>> recipes, int start, int end) =>
        recipes.Skip(Math.Max(0, start - 1)).Take(Math.Max(0, end - Math.Max(1, start))).SelectMany(recipe => recipe);

    /// <summary>How many of <paramref name="target"/> one <paramref name="id"/> takes, through its recipe when
    /// crafted. The depth bound stops a recipe cycle.</summary>
    private static int CountWithin(
        string id, string target, IReadOnlyDictionary<string, GameCatalogUpgradeView> upgrades, int depth)
    {
        if (id == target) return 1;
        if (depth > 16 || !upgrades.TryGetValue(id, out var upgrade) || !upgrade.Craftable) return 0;
        return upgrade.Recipe.Sum(ingredient => ingredient.Count * CountWithin(ingredient.Material, target, upgrades, depth + 1));
    }

    /// <summary>
    /// Indexes the account's existing goals for the "already imported" check, by slot: unit, type and current
    /// end target (<see cref="GoalTargetKey"/>; no target history is kept, so a goal edited in place matches
    /// only its current target). The first goal seen for a key wins.
    /// </summary>
    private static Dictionary<GoalKey, ExistingGoalRef> ExistingGoalsByKey(IEnumerable<Goal> existingRows)
    {
        var byKey = new Dictionary<GoalKey, ExistingGoalRef>();
        foreach (var row in existingRows)
        {
            var reference = new ExistingGoalRef(row.Id.Value, row.Config);
            byKey.TryAdd(
                new GoalKey(row.EntityType, row.EntityId, row.GoalType, GoalTargetKey.For(row)),
                reference);
        }

        return byKey;
    }

    /// <summary>Every material the account's Upgrade goals target, per unit, with the first such goal: a V1
    /// material goal for one of them is already imported.</summary>
    private static Dictionary<(UnitKey Unit, string UpgradeId), Guid> ExistingUpgradeMaterials(IEnumerable<Goal> existingRows)
    {
        var byMaterial = new Dictionary<(UnitKey, string), Guid>();
        foreach (var row in existingRows.Where(row => row.GoalType == GoalType.Upgrade))
        {
            foreach (var target in row.Config.Upgrade?.Targets ?? [])
                byMaterial.TryAdd((new UnitKey(row.EntityType, row.EntityId), target.UpgradeId), row.Id.Value);
        }

        return byMaterial;
    }

    /// <summary>The account's in-flight Ascension goals per unit: each can cover an imported goal's
    /// above-cap requirement instead of a synthesized one.</summary>
    private static Dictionary<UnitKey, List<AscensionCover>> ExistingAscensionsByUnit(IEnumerable<Goal> existingRows) =>
        existingRows
            .Where(row => row.GoalType == GoalType.Ascension
                && ProjectGoalPlanningService.IsInFlight(row.Status)
                && row.Config.Progression is not null)
            .GroupBy(row => new UnitKey(row.EntityType, row.EntityId))
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => new AscensionCover(row.Id.Value, ProgressionRules.FromWireName(row.Config.Progression!.End))).ToList());

    private static (List<TranslatedGoal> Survivors, List<(TranslatedGoal Duplicate, TranslatedGoal Survivor)> MergedInto)
        CollapseDuplicates(List<TranslatedGoal> translated)
    {
        var survivors = new List<TranslatedGoal>();
        var mergedInto = new List<(TranslatedGoal, TranslatedGoal)>();

        foreach (var group in translated.GroupBy(goal => goal.Key))
        {
            // OriginalIndex already reflects ascending V1 priority order.
            var ordered = group.OrderBy(goal => goal.OriginalIndex).ThenBy(goal => goal.Sequence).ToList();
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
                var first = ordered.MinBy(goal => goal.Config.Progression!.Start)!;
                var last = ordered.MaxBy(goal => goal.Config.Progression!.End)!;
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
            else if (group.Key.GoalType == GoalType.Upgrade)
            {
                // Material goals landing on the same unit with the same material merge into one, at the
                // earliest one's priority. Each target was sized for the unit's own ladder, so the repeated
                // material keeps the larger quantity rather than the sum.
                var targets = ordered
                    .SelectMany(goal => goal.Config.Upgrade!.Targets)
                    .GroupBy(target => target.UpgradeId, StringComparer.Ordinal)
                    .Select(targetGroup => new UpgradeMaterialTargetRequest(
                        targetGroup.Key, targetGroup.Max(target => target.Quantity)))
                    .ToList();
                survivor = ordered[0] with
                {
                    Config = new CreateGoalConfigRequest(Upgrade: new UpgradeTargetRequest(targets)),
                    Notes = JoinNotes(ordered.DistinctBy(goal => goal.OriginalIndex)),
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
                mergedInto.Add((extra, survivor));
            }
        }

        return (survivors, mergedInto);
    }

    /// <returns>The import's project, or null when nothing was committed.</returns>
    private async Task<Guid?> CreateGoalsAsync(
        ProfileId profileId,
        PlayerDataSnapshot playerSnapshot,
        List<TranslatedGoal> creatable,
        Dictionary<GoalKey, ExistingGoalRef> existingByKey,
        Dictionary<UnitKey, List<AscensionCover>> existingAscensions,
        bool synthesizePrerequisites,
        string?[] sourceIdBySlot,
        V1GoalOutcome?[] outcomeBySourceIndex,
        List<V1GoalOutcome> extraOutcomes,
        CancellationToken ct)
    {
        // Grouping by unit is still needed to compute each unit's shared prerequisites (Unlock,
        // Ascension each serve every candidate of their unit that needs them) — it no longer
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

        Guid? createdProjectId = null;
        await planning.ExecuteLockedMutationAsync(async transaction =>
        {
            // The import's own project counts toward the profile's project cap: at the cap nothing is created.
            if (await db.Projects.CountAsync(ct) >= ProjectValidation.MaxProjects)
            {
                foreach (var candidate in creatable)
                {
                    outcomeBySourceIndex[candidate.Slot] = CandidateOutcome(
                        candidate, V1GoalOutcomeStatus.Failed, "project_limit_reached",
                        $"The account already has {ProjectValidation.MaxProjects} projects, so the import could not "
                        + "create its own.", sourceIdBySlot);
                }
                return;
            }

            // Per-type room under GoalValidation.MaxGoalsPerType, read under the lock and spent as goals are
            // staged; a goal with no room left is skipped.
            var cappedCounts = await planning.CappedGoalCountsAsync(ct);
            bool TakeGoalSlot(GoalType goalType)
            {
                var count = cappedCounts.GetValueOrDefault(goalType);
                if (count >= GoalValidation.MaxGoalsPerType)
                    return false;
                cappedCounts[goalType] = count + 1;
                return true;
            }

            var now = timeProvider.GetUtcNow();
            // Each import that creates goals gets its own project, named after when it ran, so imported goals
            // stay together and apart from the user's own. It is only tracked once goals are staged (below), so
            // an import that creates nothing leaves no empty project behind.
            var project = new Project
            {
                Id = ProjectId.From(Guid.CreateVersion7()),
                ProfileId = profileId,
                Name = ImportProjectName(now),
                Status = ProjectStatus.Active,
                Type = ProjectType.Custom,
            };
            var staged = new Dictionary<int, (Goal Goal, TranslatedGoal Candidate)>();
            // Synthesized-prerequisite "Created" outcomes (Unlock/Ascension), like the `staged`
            // source-goal ones above, can't be finalized until the transaction actually commits — a
            // project-slot-conflict rollback below must still be able to turn them into failures instead of
            // reporting a goal that was never persisted.
            var stagedPrerequisites = new List<(Goal Goal, int CandidateCount)>();
            // Every goal this batch creates, tagged with a flat-order sort key (v1-goal-import: "Imported
            // goals preserve V1 priority order") instead of getting its priority assigned inline as it's
            // built. AnchorIndex is the goal's own OriginalIndex for a real imported candidate (Rank,
            // Ability, Unlock, or the unit's own imported Ascension goal — none of these are
            // synthesized, so none are pulled out of their natural V1 position), or the lowest OriginalIndex
            // among a synthesized prerequisite's actual dependents (it has no V1 position of its own — this
            // places it immediately before whichever of its dependents appears earliest). SubOrder breaks a
            // tie at the same AnchorIndex in the same order this unit's prerequisites were always built in:
            // Unlock, synthesized Ascension, then the anchoring candidate itself.
            var pending = new List<(Goal Goal, int AnchorIndex, int SubOrder, int Sequence)>();

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
                var needsUnlock = synthesizePrerequisites && NeedsUnlock(unitKey, unitCandidates, playerUnit, existingByKey);

                Guid? unlockGoalId = null;
                if (needsUnlock)
                {
                    // Validated like every other synthesized/candidate goal before persisting (unlike the
                    // synthesized Ascension path below, this one isn't structurally guaranteed valid: Unlock
                    // is Character-only and further requires catalog shard-upgrade data — see
                    // GoalTargetValidationService — neither of which NeedsUnlock checks).
                    var unlockConfig = new CreateGoalConfigRequest();
                    var unlockError = await targetValidation.ValidateAsync(
                        profileId, unitKey.EntityType, unitKey.EntityId, GoalType.Unlock, unlockConfig, ct);
                    if (unlockError is not null)
                    {
                        extraOutcomes.Add(FailedPrerequisiteOutcome(unitKey, GoalType.Unlock, unlockError));
                    }
                    else if (!TakeGoalSlot(GoalType.Unlock))
                    {
                        extraOutcomes.Add(LimitedPrerequisiteOutcome(unitKey, GoalType.Unlock));
                    }
                    else
                    {
                        // Every candidate of this unit gets a DependsOn edge to Unlock (below), so its
                        // earliest dependent is simply the unit's first candidate by V1 order.
                        var anchorIndex = unitCandidates.Min(candidate => candidate.OriginalIndex);
                        var unlockGoal = BuildGoal(profileId, unitKey, GoalType.Unlock, unlockConfig, null, prerequisiteStatus, playerUnit);
                        db.Goals.Add(unlockGoal);
                        pending.Add((unlockGoal, anchorIndex, 0, 0));
                        unlockGoalId = unlockGoal.Id.Value;
                        stagedPrerequisites.Add((unlockGoal, unitCandidates.Count));
                    }
                }

                // The unit's own imported Ascension goals (several when V1 had distinct ends) are not synthesized,
                // so each keeps its own natural V1 position. They are staged first so Rank/Ability siblings below
                // can depend on one, wherever they end up in the order; with the unit's existing in-flight
                // Ascension goals they are the covers an above-cap requirement is satisfied by.
                var covers = new List<AscensionCover>(existingAscensions.GetValueOrDefault(unitKey) ?? []);
                var ownAscensions = unitCandidates.Where(candidate => candidate.Key.GoalType == GoalType.Ascension).ToList();
                foreach (var ownAscension in ownAscensions)
                {
                    var error = await targetValidation.ValidateAsync(
                        profileId, unitKey.EntityType, unitKey.EntityId, GoalType.Ascension, ownAscension.Config, ct);
                    if (error is not null)
                    {
                        outcomeBySourceIndex[ownAscension.Slot] = FailedOutcome(ownAscension, error, sourceIdBySlot);
                        continue;
                    }

                    if (!TakeGoalSlot(GoalType.Ascension))
                    {
                        outcomeBySourceIndex[ownAscension.Slot] = LimitedOutcome(ownAscension, sourceIdBySlot);
                        continue;
                    }

                    var goal = BuildGoal(profileId, unitKey, GoalType.Ascension, ownAscension.Config, ownAscension.Notes, StatusOf(ownAscension), playerUnit);
                    if (unlockGoalId is { } unlockId) goal.DependsOn.Add(unlockId);
                    db.Goals.Add(goal);
                    pending.Add((goal, ownAscension.OriginalIndex, 3, ownAscension.Sequence));
                    staged[ownAscension.Slot] = (goal, ownAscension);
                    covers.Add(new AscensionCover(goal.Id.Value, ownAscension.Config.Progression!.End));
                }

                // Each Rank/Ability sibling above the unit's live progression depends on the covering Ascension goal
                // with the lowest end that reaches what it needs. Those no goal covers share one synthesized
                // Ascension at the lowest progression satisfying all of them, placed before the first of them.
                var coverByCandidate = new Dictionary<int, AscensionCover>();
                var uncovered = new List<(TranslatedGoal Candidate, int Need)>();
                var liveProgressionIndex = playerUnit is not null ? (int)playerUnit.ProgressionIndex : 0;
                foreach (var candidate in unitCandidates.Where(candidate => candidate.Key.GoalType is GoalType.Rank or GoalType.Ability))
                {
                    var need = NeededProgressionIndex(candidate);
                    if (need <= liveProgressionIndex)
                        continue;
                    if (covers.Where(cover => (int)cover.End >= need).OrderBy(cover => cover.End).FirstOrDefault() is { } cover)
                        coverByCandidate[candidate.Slot] = cover;
                    else
                        uncovered.Add((candidate, need));
                }

                if (synthesizePrerequisites && uncovered.Count > 0 && !TakeGoalSlot(GoalType.Ascension))
                {
                    extraOutcomes.Add(LimitedPrerequisiteOutcome(unitKey, GoalType.Ascension));
                }
                else if (synthesizePrerequisites && uncovered.Count > 0)
                {
                    var targetProgression = (UnitProgression)uncovered.Max(entry => entry.Need);
                    var startWire = CurrentProgressionWire(playerUnit);
                    var ascensionGoal = BuildGoal(
                        profileId, unitKey, GoalType.Ascension,
                        new CreateGoalConfigRequest(Progression: new ProgressionTargetRequest(
                            ProgressionRules.FromWireName(startWire), targetProgression)),
                        null, prerequisiteStatus, playerUnit);
                    if (unlockGoalId is { } unlockId) ascensionGoal.DependsOn.Add(unlockId);
                    db.Goals.Add(ascensionGoal);
                    pending.Add((ascensionGoal, uncovered.Min(entry => entry.Candidate.OriginalIndex), 1, 0));
                    stagedPrerequisites.Add((ascensionGoal, uncovered.Count));
                    foreach (var (candidate, _) in uncovered)
                        coverByCandidate[candidate.Slot] = new AscensionCover(ascensionGoal.Id.Value, targetProgression);
                }

                foreach (var candidate in unitCandidates.Except(ownAscensions))
                {
                    var dependsOn = new List<Guid>();
                    if (unlockGoalId is { } unlockId) dependsOn.Add(unlockId);

                    UnitProgression? effectiveFloor = null;
                    if (coverByCandidate.TryGetValue(candidate.Slot, out var cover))
                    {
                        dependsOn.Add(cover.GoalId);
                        effectiveFloor = cover.End;
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
                        outcomeBySourceIndex[candidate.Slot] = FailedOutcome(candidate, validationError, sourceIdBySlot);
                        continue;
                    }

                    if (!TakeGoalSlot(candidate.Key.GoalType))
                    {
                        outcomeBySourceIndex[candidate.Slot] = LimitedOutcome(candidate, sourceIdBySlot);
                        continue;
                    }

                    var goal = BuildGoal(profileId, unitKey, candidate.Key.GoalType, candidate.Config, candidate.Notes, StatusOf(candidate), playerUnit);
                    goal.DependsOn = dependsOn;
                    db.Goals.Add(goal);
                    pending.Add((goal, candidate.OriginalIndex, 3, candidate.Sequence));
                    staged[candidate.Slot] = (goal, candidate);
                }
            }

            if (pending.Count > 0)
            {
                db.Projects.Add(project);
            }

            // Flat pass: assign priorities in (AnchorIndex, SubOrder) order — this is where V1's priority
            // sequence (and each synthesized prerequisite's "immediately before its earliest dependent"
            // placement) actually becomes the persisted project-goal order.
            foreach (var (goal, _, _, _) in pending.OrderBy(entry => entry.AnchorIndex).ThenBy(entry => entry.SubOrder).ThenBy(entry => entry.Sequence))
            {
                await order.AppendAsync(goal, ct);
                db.ProjectGoals.Add(ProjectGoalPlanningService.CreateMembership(project.Id, goal.Id, now));
            }

            // The account-wide dedupe pass above read the existing goals before the profile lock, so a
            // goal created in between could take a slot this batch wants; re-check under the lock. On a
            // conflict, report every still-pending candidate as failed rather than saving a duplicate.
            if (await planning.FindConflictAsync(pending.Select(entry => entry.Goal).ToList(), ct) is not null)
            {
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                foreach (var (index, _) in staged)
                {
                    outcomeBySourceIndex[index] ??= new V1GoalOutcome(
                        V1GoalOutcomeStatus.Failed, "project_slot_conflict",
                        "Another goal for this unit, type and target was created concurrently.",
                        null, null, null, null, sourceIdBySlot[index]);
                }
                foreach (var (goal, _) in stagedPrerequisites)
                {
                    extraOutcomes.Add(new V1GoalOutcome(
                        V1GoalOutcomeStatus.Failed, "project_slot_conflict",
                        "Another goal for this unit and type was created concurrently.",
                        goal.EntityType, goal.EntityId, goal.GoalType, null, null));
                }
                return;
            }

            await db.SaveChangesAsync(ct);
            await order.CompleteAsync(ct);
            await transaction.CommitAsync(ct);
            if (pending.Count > 0)
            {
                createdProjectId = project.Id.Value;
            }

            foreach (var (index, (goal, candidate)) in staged)
            {
                outcomeBySourceIndex[index] = new V1GoalOutcome(
                    V1GoalOutcomeStatus.Created,
                    candidate.CreatedCode ?? "goal_created",
                    candidate.CreatedCode is null
                        ? "Imported from V1."
                        : "Imported from V1 onto a suggested unit, since the V1 goal had none. Move it if another unit should hold it.",
                    goal.EntityType,
                    goal.EntityId,
                    goal.GoalType,
                    goal.Id.Value,
                    sourceIdBySlot[index],
                    candidate.MaterialId,
                    candidate.LinkedSourceGoalIds,
                    candidate.MaterialQuantity);
            }
            foreach (var (goal, candidateCount) in stagedPrerequisites)
            {
                extraOutcomes.Add(PrerequisiteOutcome(goal, candidateCount));
            }
        }, ct);
        return createdProjectId;
    }

    /// <summary>Whether a Character absent from the roster needs a synthesized Unlock: not when the unit's
    /// imported goals include one, or the account already has one (v1-goal-import: "Missing prerequisites are
    /// created automatically by the same rules as manual creation"). Unlock is only ever valid for a Character
    /// (GoalTargetValidationService), so a Mow missing from the roster never gets one.</summary>
    private static bool NeedsUnlock(
        UnitKey unitKey, List<TranslatedGoal> unitCandidates, PlayerBaseUnitRecord? playerUnit,
        Dictionary<GoalKey, ExistingGoalRef> existingByKey) =>
        unitKey.EntityType == GoalEntityType.Character
            && playerUnit is null
            && unitCandidates.All(candidate => candidate.Key.GoalType != GoalType.Unlock)
            && !existingByKey.ContainsKey(new GoalKey(unitKey.EntityType, unitKey.EntityId, GoalType.Unlock));

    /// <summary>The lowest progression index a Rank or Ability target is reachable at (never Level: the level a
    /// target needs is intrinsic to it).</summary>
    private static int NeededProgressionIndex(TranslatedGoal candidate) =>
        candidate.Key.GoalType == GoalType.Rank
            ? (int)ProgressionRules.MinimumProgressionForRank((UnitRank)candidate.Config.Rank!.End)
            : (int)ProgressionRules.MinimumProgressionForAbilityLevel(
                Math.Max(candidate.Config.Ability!.ActiveEnd, candidate.Config.Ability.PassiveEnd));

    private static Goal BuildGoal(
        ProfileId profileId, UnitKey unitKey, GoalType goalType, CreateGoalConfigRequest config, string? notes,
        GoalStatus status, PlayerBaseUnitRecord? playerUnit)
    {
        var goal = new Goal(unitKey.EntityType, unitKey.EntityId, goalType)
        {
            Id = GoalId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            Status = status,
            Notes = notes,
            Config = GoalMapper.MapConfig(config),
            Snapshot = BuildSnapshot(playerUnit),
        };
        return goal;
    }

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
        V1GoalOutcomeStatus.Created,
        "prerequisite_added",
        $"Automatically added because it is required by {candidateCount} imported goal(s) for this unit.",
        prerequisite.EntityType,
        prerequisite.EntityId,
        prerequisite.GoalType,
        prerequisite.Id.Value,
        null);

    /// <summary>Reports a synthesized Unlock/Ascension prerequisite that <see cref="GoalTargetValidationService"/>
    /// rejected (e.g. Unlock for a Mow) — the prerequisite
    /// is not persisted, and dependents proceed without a dependency edge to it.</summary>
    private static V1GoalOutcome FailedPrerequisiteOutcome(UnitKey unitKey, GoalType goalType, string message) => new(
        V1GoalOutcomeStatus.Failed,
        "prerequisite_rejected",
        message,
        unitKey.EntityType,
        unitKey.EntityId,
        goalType,
        null,
        null);

    private static V1GoalOutcome FailedOutcome(TranslatedGoal candidate, string message, string?[] sourceIdBySlot) =>
        CandidateOutcome(candidate, V1GoalOutcomeStatus.Failed, "target_rejected", message, sourceIdBySlot);

    private const string GoalLimitCode = "goal_limit_reached";

    private static string GoalLimitMessage(GoalType goalType) =>
        $"The account already has {GoalValidation.MaxGoalsPerType} {goalType} goals that are not archived.";

    private static V1GoalOutcome LimitedOutcome(TranslatedGoal candidate, string?[] sourceIdBySlot) =>
        CandidateOutcome(
            candidate, V1GoalOutcomeStatus.Skipped, GoalLimitCode, GoalLimitMessage(candidate.Key.GoalType), sourceIdBySlot);

    /// <summary>A synthesized prerequisite left out because its goal type is at the cap; dependents proceed
    /// without the edge, as for a rejected one.</summary>
    private static V1GoalOutcome LimitedPrerequisiteOutcome(UnitKey unitKey, GoalType goalType) => new(
        V1GoalOutcomeStatus.Skipped, GoalLimitCode, GoalLimitMessage(goalType),
        unitKey.EntityType, unitKey.EntityId, goalType, null, null);

    private static V1GoalOutcome CandidateOutcome(
        TranslatedGoal candidate, V1GoalOutcomeStatus status, string code, string message, string?[] sourceIdBySlot) => new(
        status,
        code,
        message,
        candidate.Key.EntityType,
        candidate.Key.EntityId,
        candidate.Key.GoalType,
        null,
        sourceIdBySlot[candidate.Slot],
        candidate.MaterialId,
        candidate.LinkedSourceGoalIds,
        candidate.MaterialQuantity);

    private static V1GoalOutcome NotImported(
        V1Goal sourceGoal, GoalEntityType? entityType, string? entityId, string code, string message, string? upgradeId = null) =>
        new(V1GoalOutcomeStatus.Failed, code, message, entityType, entityId, null, null, sourceGoal.Id, upgradeId);

    private static V1GoalOutcome Skipped(
        V1Goal sourceGoal, GoalEntityType entityType, string entityId, GoalType goalType, string code, string message) =>
        new(V1GoalOutcomeStatus.Skipped, code, message, entityType, entityId, goalType, null, sourceGoal.Id);

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
    /// the user had in daily planning imports Active, one they had excluded imports Paused, whatever project
    /// it is filed into (goal-lifecycle-status).</summary>
    private static GoalStatus StatusOf(TranslatedGoal goal) =>
        goal.InDailyPlanning ? GoalStatus.Active : GoalStatus.Paused;

    /// <summary>"V1 import · 23 Oct, 13:45 UTC": the time tells two imports on one day apart. The server has
    /// no user locale or time zone, so the stamp is invariant and UTC.</summary>
    internal static string ImportProjectName(DateTimeOffset now) =>
        string.Create(CultureInfo.InvariantCulture, $"V1 import · {now.UtcDateTime:d MMM, HH:mm} UTC");

    /// <summary><paramref name="TargetKey"/> is the <see cref="GoalTargetKey"/> (null for Unlock), so distinct end
    /// targets for one unit and type are distinct keys (imported as separate milestones) while an exact duplicate
    /// shares a key (merged). A V1 material goal's Upgrade candidate is keyed by its material instead.</summary>
    private sealed record GoalKey(GoalEntityType EntityType, string EntityId, GoalType GoalType, string? TargetKey = null);

    private sealed record AscensionCover(Guid GoalId, UnitProgression End);

    private sealed record ExistingGoalRef(Guid Id, GoalConfig Config);

    private sealed record UnitKey(GoalEntityType EntityType, string EntityId);

    /// <summary><see cref="InDailyPlanning"/> carries V1's per-goal <c>dailyRaids</c> choice through to the
    /// created goal's status: in daily planning becomes <see cref="GoalStatus.Active"/>, out of it becomes
    /// <see cref="GoalStatus.Paused"/>.
    /// <see cref="OriginalIndex"/> is the source goal's V1 priority position, which orders the created goal;
    /// <see cref="Slot"/> is where its outcome is reported — the source goal's own slot, or a further one for
    /// each extra Upgrade goal a pre-farm goal fans out into (<see cref="Sequence"/> orders those among
    /// themselves). The material fields are set only on an Upgrade goal translated from a V1 material goal.</summary>
    private sealed record TranslatedGoal(
        GoalKey Key, CreateGoalConfigRequest Config, int OriginalIndex, string? Notes, bool InDailyPlanning)
    {
        public int? SlotOverride { get; init; }
        public int Slot => SlotOverride ?? OriginalIndex;
        public int Sequence { get; init; }
        public string? CreatedCode { get; init; }
        public string? MaterialId { get; init; }
        public int? MaterialQuantity { get; init; }
        public IReadOnlyList<string>? LinkedSourceGoalIds { get; init; }
    }

    /// <summary>Suggests the unit to hold a V1 material goal's Upgrade goal. Candidates, in order: the units of
    /// the profile's own imported unit goals (V1 priority order, on the roster or not — an imported Unlock goal's
    /// unit counts), then the account's roster characters, then its roster Mows — each only if the material is
    /// relevant to it (the same rule manual Upgrade-goal creation validates). Among those, a unit whose existing
    /// Upgrade goal already targets the material wins, else the first relevant candidate. Null when no
    /// candidate uses the material, which is also the <c>no_upgrade_owner</c> condition.</summary>
    private sealed class UpgradeOwnerResolver(
        GameCatalogSnapshot catalog,
        PlayerDataSnapshot playerSnapshot,
        IReadOnlyList<UnitKey> preferred,
        Dictionary<(UnitKey Unit, string UpgradeId), Guid> existingMaterials)
    {
        private readonly Dictionary<UnitKey, IReadOnlySet<string>> relevantByUnit = new();
        private List<UnitKey>? candidates;

        public UnitKey? Resolve(string upgradeId)
        {
            candidates ??= BuildCandidates();
            var relevant = candidates.Where(unit => Relevant(unit).Contains(upgradeId)).ToList();
            return relevant.FirstOrDefault(unit => existingMaterials.ContainsKey((unit, upgradeId))) ?? relevant.FirstOrDefault();
        }

        public bool Uses(UnitKey unit, string upgradeId) => Relevant(unit).Contains(upgradeId);

        private List<UnitKey> BuildCandidates() =>
            preferred
                .Concat(playerSnapshot.Characters.Select(item => new UnitKey(GoalEntityType.Character, item.UnitId.Value)))
                .Concat(playerSnapshot.Mows.Select(item => new UnitKey(GoalEntityType.Mow, item.UnitId.Value)))
                .Distinct()
                .ToList();

        private IReadOnlySet<string> Relevant(UnitKey unit)
        {
            if (!relevantByUnit.TryGetValue(unit, out var relevant))
            {
                relevant = unit.EntityType == GoalEntityType.Character
                    ? catalog.CharacterRelevantUpgradeIds(unit.EntityId)
                    : catalog.MowRelevantUpgradeIds(unit.EntityId);
                relevantByUnit[unit] = relevant;
            }

            return relevant;
        }
    }

}

/// <summary>One outcome per source V1 goal (v1-goal-import spec), plus one per synthesized prerequisite. <see cref="Status"/> is one of "Created", "Skipped", or
/// "Failed"; <see cref="Code"/> is a stable machine-readable discriminator finer than <see cref="Status"/>
/// (e.g. "goal_already_exists", "duplicate_goal_merged", "unsupported_goal_type", "prerequisite_added").
/// <see cref="GoalId"/> is the V2 goal this outcome created or matched, when one applies.
/// <see cref="SourceGoalId"/> is the originating V1 goal's id, when this outcome corresponds to one (null
/// for a synthesized prerequisite, which has no single V1 source). <see cref="UpgradeId"/> is the upgrade
/// material a V1 material goal (type 6 or 7) named, <see cref="LinkedSourceGoalIds"/> the V1 ids of the
/// imported goals a pre-farm goal was linked to, and <see cref="UpgradeQuantity"/> the quantity its Upgrade
/// goal targets — set only on those outcomes. A pre-farm goal reports one outcome per unit it became an
/// Upgrade goal on, all carrying its <see cref="SourceGoalId"/>.</summary>
public sealed record V1GoalOutcome(
    V1GoalOutcomeStatus Status,
    string Code,
    string Message,
    GoalEntityType? EntityType,
    string? EntityId,
    GoalType? GoalType,
    Guid? GoalId,
    string? SourceGoalId,
    [property: JsonRequired] string? UpgradeId = null,
    [property: JsonRequired] IReadOnlyList<string>? LinkedSourceGoalIds = null,
    [property: JsonRequired] int? UpgradeQuantity = null
);

public sealed record V1GoalImportResult(IReadOnlyList<V1GoalOutcome> Outcomes, bool Refused)
{
    /// <summary>The project this run created its goals in; null when it created none.</summary>
    public Guid? ProjectId { get; init; }

    public static V1GoalImportResult CreateRefused() => new(
        [
            new V1GoalOutcome(
                V1GoalOutcomeStatus.Failed,
                "player_data_required",
                "Player data must be synced before goals can be imported.",
                null, null, null, null, null),
        ],
        true);
}
