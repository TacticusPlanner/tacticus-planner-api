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
            .SelectMany(group => group.Battles.Select(battle => BuildBattleView(battle, group, dropChanceById)))
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
                group.Battles.Select(battle => battle.Id).ToArray(),
                group.Number))
            .ToArray();

    private static GameCatalogCampaignBattleView BuildBattleView(
        GameCatalogCampaignBattle battle,
        GameCatalogCampaignGroup group,
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
            group.GroupId,
            battle.Type,
            battle.Challenge,
            battle.EnergyCost,
            battle.NodeNumber,
            battle.BattleIndex,
            battle.Slots,
            DailyAttemptsFor(battle, group),
            new GameCatalogCampaignRewardsView(battle.Rewards.Guaranteed, potential, battle.Rewards.Medals, battle.Rewards.Milestones),
            battle.EnemyPower,
            group.AlliesAlliance,
            group.AlliesFactions,
            battle.EnemiesAlliances,
            battle.EnemiesFactions,
            battle.EnemiesTotal,
            battle.EnemiesTypes,
            battle.DetailedEnemyTypes,
            battle.LightningVictoryTurns);
    }

    // The early Indomitus nodes I06-I14 (the 3-energy ones) cap at 3 attempts/day (confirmed by the product owner).
    private const string IndomitusGroupId = "campaign1";
    private const int IndomitusLowCapFirstNode = 6;
    private const int IndomitusLowCapLastNode = 14;

    private static int DailyAttemptsFor(GameCatalogCampaignBattle battle, GameCatalogCampaignGroup group) =>
        group.GroupId == IndomitusGroupId && battle.Type == "Standard" && !battle.Challenge
            && battle.NodeNumber is >= IndomitusLowCapFirstNode and <= IndomitusLowCapLastNode
            ? 3
            : DailyAttemptsForType(battle.Type);

    // Fixed daily attempt caps per Tacticus campaign type (product-confirmed): Elite/EliteMirror nodes
    // cap at 6 attempts/day, every other type (Standard/Mirror/Extremis) caps at 10, except the low-cap
    // Indomitus nodes above.
    private static int DailyAttemptsForType(string type) => type switch
    {
        "Elite" or "EliteMirror" => 6,
        _ => 10,
    };
}
