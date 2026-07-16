using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

public sealed class GoalTargetValidationService(PlannerDbContext db, IGameCatalogProvider catalog)
{
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

        if (goalType == GoalType.Unlock)
        {
            if (entityType != GoalEntityType.Character || !catalog.Current.IsUnlockEligible(entityId))
                return "Unlock is unavailable because the catalog has no shard-upgrade data for this character.";
            if (playerUnit is not null) return "The selected character is already unlocked.";
        }

        if (goalType == GoalType.Rank)
        {
            if (entityType != GoalEntityType.Character || config.Rank is null) return "Rank requires a character rank target.";
            var current = (int?)playerCharacter?.Rank ?? config.Rank.Start;
            if (config.Rank.Start < current) return "The starting rank cannot be lower than the current rank.";
            if (config.Rank.End <= Math.Max(config.Rank.Start, current) || config.Rank.End > (int)UnitRank.Adamantine3)
                return "The target rank must be above the effective starting rank and within the rank ladder.";
        }

        if (goalType == GoalType.Ascension)
        {
            if (config.Progression is null) return "Ascension requires a progression target.";
            var start = GameCatalogGoalLookups.ProgressionIndex(config.Progression.Start);
            var end = GameCatalogGoalLookups.ProgressionIndex(config.Progression.End);
            var current = (int?)playerUnit?.ProgressionIndex ?? start;
            if (start < current) return "The starting progression cannot be lower than current progression.";
            if (end <= Math.Max(start, current)) return "The target progression must be above the effective start.";
            if (config.AscensionFarming is { } farming)
            {
                var available = character?.ShardLocations.Select(location => location.BattleId).ToHashSet(StringComparer.Ordinal) ?? [];
                if (farming.ShardBattleIds.Any(id => !available.Contains(id.Value))
                    || farming.MythicShardBattleIds.Any(id => !available.Contains(id.Value)))
                    return "An ascension farming node is not available for this character.";
            }
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
            var cap = playerUnit is null
                ? 60
                : GameCatalogGoalLookups.AbilityCapForRarity(RarityFor(playerUnit.ProgressionIndex));
            if (config.Ability.ActiveEnd > cap || config.Ability.PassiveEnd > cap)
                return $"Ability targets cannot exceed the current rarity cap of {cap}.";
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

    private static string RarityFor(UnitProgression progression) => (int)progression switch
    {
        <= 2 => "Common",
        <= 5 => "Uncommon",
        <= 8 => "Rare",
        <= 11 => "Epic",
        <= 15 => "Legendary",
        _ => "Mythic",
    };
}
