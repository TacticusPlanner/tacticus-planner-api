using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameDomain;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

public sealed class GoalTargetValidationService(PlannerDbContext db, IGameCatalogProvider catalog)
{
    /// <summary>The highest character level a Level goal can ever target (Adamantine2, the frontend's
    /// current rank-ladder ceiling — mirrors the frontend's own MAX_CHARACTER_LEVEL in
    /// goal-validation.ts).</summary>
    private const int MaxCharacterLevel = 60;

    /// <summary>Returns an error message when the target is invalid, or null when it's valid.</summary>
    public async Task<string?> ValidateAsync(
        ProfileId profileId,
        GoalEntityType entityType,
        string entityId,
        GoalType goalType,
        CreateGoalConfigRequest config,
        CancellationToken ct)
    {
        var character = catalog.Current.CharacterViews.FirstOrDefault(item => item.Id == entityId);
        var mow = catalog.Current.MowList.FirstOrDefault(item => item.Id == entityId);
        if (entityType == GoalEntityType.Character && character is null
            || entityType == GoalEntityType.Mow && mow is null)
        {
            return "The selected unit is not present in the Game Catalog.";
        }

        var snapshot = await db.PlayerDataSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == profileId, ct);
        var playerCharacter = snapshot?.Characters.FirstOrDefault(item => item.UnitId.Value == entityId);
        var playerMow = snapshot?.Mows.FirstOrDefault(item => item.UnitId.Value == entityId);
        var playerUnit = (PlayerBaseUnitRecord?)playerCharacter ?? playerMow;

        // Each goal type is only ever valid for one entity type (Rank/Unlock/Level: Character only;
        // Ascension/Ability/Upgrade: Character or Mow) — checked once
        // up front so every branch below can assume the pairing already makes sense.
        var entityTypeMismatch = (goalType, entityType) switch
        {
            (GoalType.Rank or GoalType.Unlock or GoalType.Level, not GoalEntityType.Character) => true,
            _ => false,
        };
        if (entityTypeMismatch)
            return "This goal type is not valid for the selected entity type.";

        if (goalType == GoalType.Unlock)
        {
            if (!catalog.Current.IsUnlockEligible(entityId))
                return "Unlock is unavailable because the catalog has no shard-upgrade data for this character.";
            if (playerUnit is not null) return "The selected character is already unlocked.";
        }

        if (goalType == GoalType.Rank)
        {
            if (config.Rank is null) return "Rank requires a character rank target.";
            var current = (int?)playerCharacter?.Rank ?? config.Rank.Start;
            if (config.Rank.Start < current) return "The starting rank cannot be lower than the current rank.";
            if (config.Rank.End <= Math.Max(config.Rank.Start, current) || config.Rank.End > (int)UnitRank.Adamantine3)
                return "The target rank must be above the effective starting rank and within the rank ladder.";
        }

        if (goalType == GoalType.Ascension)
        {
            if (config.Progression is null) return "Ascension requires a progression target.";
            var start = ProgressionRules.ProgressionIndex(config.Progression.Start);
            var end = ProgressionRules.ProgressionIndex(config.Progression.End);
            var current = (int?)playerUnit?.ProgressionIndex ?? start;
            if (start < current) return "The starting progression cannot be lower than current progression.";
            if (end <= Math.Max(start, current)) return "The target progression must be above the effective start.";
        }

        if (goalType == GoalType.Ability)
        {
            if (config.Ability is null) return "Ability requires an ability target.";
            var first = playerUnit?.Abilities.ElementAtOrDefault(0)?.Level ?? config.Ability.ActiveStart;
            var second = playerUnit?.Abilities.ElementAtOrDefault(1)?.Level ?? config.Ability.PassiveStart;
            if (config.Ability.ActiveStart < first || config.Ability.PassiveStart < second)
                return "Ability starting levels cannot be lower than current levels.";
            if (config.Ability.ActiveEnd <= config.Ability.ActiveStart
                && config.Ability.PassiveEnd <= config.Ability.PassiveStart)
                return "At least one ability target must be above its effective start.";
            var cap = ProgressionRules.AbilityCapForProgression(
                playerUnit?.ProgressionIndex ?? UnitProgression.MythicMythicWings);
            if (config.Ability.ActiveEnd > cap || config.Ability.PassiveEnd > cap)
                return $"Ability targets cannot exceed the current rarity cap of {cap}.";
        }

        if (goalType == GoalType.Upgrade)
        {
            if (config.Upgrade is not { Targets.Count: > 0 })
                return "Upgrade requires at least one target.";
            var relevant = entityType == GoalEntityType.Character
                ? catalog.Current.CharacterRelevantUpgradeIds(entityId)
                : catalog.Current.MowRelevantUpgradeIds(entityId);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in config.Upgrade.Targets)
            {
                if (target.Quantity <= 0) return "Every upgrade target quantity must be positive.";
                if (!seen.Add(target.UpgradeId)) return "Upgrade targets must use unique upgrade ids.";
                if (!relevant.Contains(target.UpgradeId))
                    return "An upgrade target is not relevant to the selected unit's own requirements.";
            }
        }

        if (goalType == GoalType.Level)
        {
            if (config.Level is null) return "Level requires a target level.";
            var current = playerCharacter?.XpLevel ?? config.Level.Start;
            if (config.Level.Start < current) return "The starting level cannot be lower than the current level.";
            if (config.Level.End <= Math.Max(config.Level.Start, current) || config.Level.End > MaxCharacterLevel)
                return $"The target level must be above the effective starting level and no higher than {MaxCharacterLevel}.";
        }

        if (AcquisitionSourceRules.SemanticError(
                config.AcquisitionSources,
                goalType,
                entityType,
                RegularShardBattleIds(character),
                MythicShardBattleIds(character),
                catalog.Current.ShopViews.Select(shop => shop.Id).ToHashSet(StringComparer.Ordinal))
            is { } acquisitionError)
        {
            return acquisitionError;
        }

        var strategy = Enum.TryParse<FarmingStrategy>(config.FarmingStrategy, true, out var parsed)
            ? parsed
            : FarmingStrategy.TotalUpgrades;
        if (strategy != FarmingStrategy.TotalUpgrades
            && goalType != GoalType.Rank
            && !(goalType == GoalType.Ability && entityType == GoalEntityType.Mow))
            return "Farming strategy is supported only for Character Rank and Machine of War Ability goals.";

        return null;
    }

    /// <summary>Re-validates an acquisition-source set against the same catalog rules
    /// <see cref="ValidateAsync"/> applies at creation — used by the update endpoint.</summary>
    public string? ValidateAcquisitionSources(
        GoalType goalType,
        GoalEntityType entityType,
        string entityId,
        IReadOnlyList<AcquisitionSourceRequest>? sources)
    {
        if (AcquisitionSourceRules.ShapeError(sources) is { } shapeError) return shapeError;

        var character = catalog.Current.CharacterViews.FirstOrDefault(item => item.Id == entityId);
        return AcquisitionSourceRules.SemanticError(
            sources,
            goalType,
            entityType,
            RegularShardBattleIds(character),
            MythicShardBattleIds(character),
            catalog.Current.ShopViews.Select(shop => shop.Id).ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>Re-validates a farming-location override against the same catalog rule
    /// <see cref="ValidateAsync"/> applies at creation — currently only meaningful for Unlock goals (the
    /// only goal type whose <c>FarmingLocationIds</c> is create-time validated).</summary>
    public string? ValidateFarmingLocationOverride(GoalType goalType, string entityId, List<string>? farmingLocationIds)
    {
        if (goalType != GoalType.Unlock || farmingLocationIds is not { Count: > 0 })
        {
            return null;
        }

        var character = catalog.Current.CharacterViews.FirstOrDefault(item => item.Id == entityId);
        var availableRegular = RegularShardBattleIds(character);
        return farmingLocationIds.Any(id => !availableRegular.Contains(id))
            ? "An unlock shard location is not available for this character."
            : null;
    }

    private static HashSet<string> RegularShardBattleIds(GameCatalogCharacterView? character) =>
        character?.ShardLocations.Where(location => !location.IsMythic).Select(location => location.BattleId)
            .ToHashSet(StringComparer.Ordinal) ?? [];

    private static HashSet<string> MythicShardBattleIds(GameCatalogCharacterView? character) =>
        character?.ShardLocations.Where(location => location.IsMythic).Select(location => location.BattleId)
            .ToHashSet(StringComparer.Ordinal) ?? [];

}
