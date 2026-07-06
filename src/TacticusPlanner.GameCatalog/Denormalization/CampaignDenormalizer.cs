using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    // campaign-battles: every battle flattened across all groups, each carrying its campaignGroupId and
    // keyed (downstream) by its globally-unique battle id.
    public static IReadOnlyList<GameCatalogCampaignBattleView> BuildCampaignBattles(
        IReadOnlyDictionary<string, GameCatalogCampaignGroup> campaignGroups,
        IReadOnlyList<GameCatalogDropChance> dropChances)
    {
        var dropChanceById = BuildDropChanceIndex(dropChances);

        return campaignGroups
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .SelectMany(group => group.Battles.Select(battle => BuildBattleView(battle, group.GroupId, dropChanceById)))
            .ToArray();
    }

    // campaign-definitions: one record per group with its metadata plus the ids of its battles (the battle
    // bodies live in the campaign-battles dataset).
    public static IReadOnlyList<GameCatalogCampaignDefinitionView> BuildCampaignDefinitions(
        IReadOnlyDictionary<string, GameCatalogCampaignGroup> campaignGroups) =>
        campaignGroups
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .Select(group => new GameCatalogCampaignDefinitionView(
                group.GroupId,
                group.Faction,
                group.ReleaseType,
                group.CoreCharacters,
                group.Types,
                group.Battles.Select(battle => battle.Id).ToArray()))
            .ToArray();

    private static GameCatalogCampaignBattleView BuildBattleView(
        GameCatalogCampaignBattle battle,
        string campaignGroupId,
        Dictionary<string, GameCatalogDropChance> dropChanceById)
    {
        var potential = battle.Rewards.Potential.Select(reward =>
        {
            if (reward.ChanceId is not null && dropChanceById.TryGetValue(reward.ChanceId, out var chance))
            {
                return new GameCatalogCampaignPotentialRewardView(reward.Id, reward.ChanceId, chance.RewardKind,
                    chance.Numerator, chance.Denominator, chance.EffectiveRate);
            }

            return new GameCatalogCampaignPotentialRewardView(reward.Id, reward.ChanceId, null, null, null, null);
        }).ToArray();

        return new GameCatalogCampaignBattleView(
            battle.Id,
            campaignGroupId,
            battle.Type,
            battle.Challenge,
            battle.EnergyCost,
            battle.NodeNumber,
            battle.Slots,
            new GameCatalogCampaignRewardsView(battle.Rewards.Guaranteed, potential),
            battle.EnemyPower,
            battle.EnemiesAlliances,
            battle.EnemiesFactions,
            battle.EnemiesTotal,
            battle.EnemiesTypes,
            battle.DetailedEnemyTypes);
    }
}
