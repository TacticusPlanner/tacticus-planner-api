using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameDomain;

namespace TacticusPlanner.GameCatalog;

/// <summary>Server-authoritative catalog rules shared by goal validation and estimation. Pure
/// progression/ability ordering (no catalog data involved) lives in <see cref="ProgressionRules"/>
/// (<c>TacticusPlanner.GameDomain</c>) instead — see that class for <c>ProgressionOrder</c>/
/// <c>ProgressionIndex</c>/<c>AbilityCapForRarity</c>, re-exposed here only where catalog data is also
/// needed.</summary>
public static class GameCatalogGoalLookups
{
    public static bool IsUnlockEligible(this GameCatalogSnapshot catalog, string characterId) =>
        catalog.CharacterViews.Any(character =>
            character.Id == characterId && character.ShardLocations.Count > 0);

    public static GameCatalogRewardRange OnslaughtReward(
        this GameCatalogSnapshot catalog,
        string sector,
        int tier,
        string rarity,
        bool mythicShards)
    {
        var reward = catalog.OnslaughtRewards.SingleOrDefault(item =>
            string.Equals(item.Sector, sector, StringComparison.OrdinalIgnoreCase) && item.Tier == tier)
            ?? throw new ArgumentOutOfRangeException(nameof(sector), $"Unknown Onslaught progress {sector} {tier}.");

        if (mythicShards)
        {
            return reward.Mythic;
        }

        var index = rarity.ToLowerInvariant() switch
        {
            "common" => 0,
            "uncommon" => 1,
            "rare" => 2,
            "epic" => 3,
            "legendary" => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(rarity), rarity, "Unsupported regular shard rarity."),
        };
        return reward.Regular[index];
    }

    public static IReadOnlyList<string> RankOrder(this GameCatalogSnapshot catalog, string characterId) =>
        catalog.CharacterViews.FirstOrDefault(character => character.Id == characterId)?.RankUpUpgrades
            .Select(step => step.Rank)
            .ToArray()
        ?? [];

    /// <summary>The upgrade ids relevant to a Character's own progression — its whole rank ladder — used
    /// to bound what an Upgrade goal on that character may target. Empty for an unknown id.
    /// This project has no reference to the Goals domain, so entity-type branching lives in the
    /// caller (<c>GoalTargetValidationService</c>); this and <see cref="MowRelevantUpgradeIds"/> are
    /// its two per-entity-type halves.</summary>
    public static IReadOnlySet<string> CharacterRelevantUpgradeIds(this GameCatalogSnapshot catalog, string characterId)
    {
        var character = catalog.CharacterViews.FirstOrDefault(item => item.Id == characterId);
        return (character?.RankUpUpgrades.SelectMany(step => step.UpgradeIds) ?? [])
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The upgrade ids relevant to a Mow's own progression — its whole primary+secondary
    /// ability ladder. Empty for an unknown id. See <see cref="CharacterRelevantUpgradeIds"/>.</summary>
    public static IReadOnlySet<string> MowRelevantUpgradeIds(this GameCatalogSnapshot catalog, string mowId)
    {
        var mow = catalog.MowList.FirstOrDefault(item => item.Id == mowId);
        if (mow is null) return new HashSet<string>(StringComparer.Ordinal);
        return mow.PrimaryAbility.Recipes.SelectMany(recipe => recipe)
            .Concat(mow.SecondaryAbility.Recipes.SelectMany(recipe => recipe))
            .ToHashSet(StringComparer.Ordinal);
    }

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>
        UpgradeRequirementsByRarity(
            this GameCatalogSnapshot catalog,
            string characterId,
            int startRankIndex,
            int endRankIndex)
    {
        var character = catalog.CharacterViews.FirstOrDefault(item => item.Id == characterId)
            ?? throw new ArgumentOutOfRangeException(nameof(characterId), characterId, "Unknown character.");
        var upgrades = catalog.UpgradeViews.ToDictionary(item => item.Id, StringComparer.Ordinal);

        return character.RankUpUpgrades
            .Skip(Math.Max(0, startRankIndex))
            .Take(Math.Max(0, endRankIndex - startRankIndex))
            .SelectMany(step => step.UpgradeIds)
            .Where(upgrades.ContainsKey)
            .GroupBy(id => upgrades[id].Rarity, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, int>)group
                    .GroupBy(id => id, StringComparer.Ordinal)
                    .ToDictionary(items => items.Key, items => items.Count(), StringComparer.Ordinal),
                StringComparer.Ordinal);
    }
}
