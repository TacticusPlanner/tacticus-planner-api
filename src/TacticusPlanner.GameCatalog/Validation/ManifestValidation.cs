using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private static readonly HashSet<string> ValidRarities =
        new(StringComparer.Ordinal) { "Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythic" };

    private static void ValidateManifestDatasets(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors)
    {
        errors.AddRange(GameCatalogDatasets.Served
            .Where(servedDataset => !snapshot.DatasetHashes.ContainsKey(servedDataset))
            .Select(servedDataset => new GameCatalogValidationError(
                servedDataset, "MissingDataset", $"Served dataset '{servedDataset}' is missing.")));
    }

    private static void ValidateServedProjections(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors)
    {
        RequireNonEmpty(GameCatalogDatasets.Characters, snapshot.CharacterViews.Count, errors);
        RequireNonEmpty(GameCatalogDatasets.Npcs, snapshot.NpcList.Count, errors);
        RequireNonEmpty(GameCatalogDatasets.Mows, snapshot.MowList.Count, errors);
        RequireNonEmpty(GameCatalogDatasets.MowUpgradeCostsServed, snapshot.MowUpgradeCostViews.Count, errors);
        RequireNonEmpty(GameCatalogDatasets.AscensionCostsServed, snapshot.AscensionCostViews.Count, errors);
        RequireNonEmpty(GameCatalogDatasets.UnlockShardCostsServed, snapshot.UnlockShardCostViews.Count, errors);
        RequireNonEmpty(GameCatalogDatasets.OnslaughtRewards, snapshot.OnslaughtRewards.Count, errors);

        errors.AddRange(snapshot.OnslaughtRewards
            .Where(reward => reward.Tier is < 1 or > 3 || reward.Regular.Count != 5
                || reward.Regular.Append(reward.Mythic).Any(range => range.Min <= 0 || range.Max < range.Min))
            .Select(reward => new GameCatalogValidationError(
                GameCatalogDatasets.OnslaughtRewards,
                "InvalidRewardRange",
                $"Onslaught reward '{reward.Id}' has an invalid tier or reward range.")));

        errors.AddRange(snapshot.OnslaughtRewards
            .Where(reward => reward.Badges.Count == 0
                || reward.Badges.Any(badge => badge.Amount <= 0 || !ValidRarities.Contains(badge.Rarity)))
            .Select(reward => new GameCatalogValidationError(
                GameCatalogDatasets.OnslaughtRewards,
                "InvalidBadgeReward",
                $"Onslaught reward '{reward.Id}' has an invalid badge rarity or amount.")));
        RequireNonEmpty(GameCatalogDatasets.Upgrades, snapshot.UpgradeViews.Count, errors);
        RequireNonEmpty(GameCatalogDatasets.Equipment, snapshot.EquipmentViews.Count, errors);
        RequireNonEmpty(GameCatalogDatasets.CampaignBattles, snapshot.CampaignBattleViews.Count, errors);
        RequireNonEmpty(GameCatalogDatasets.CampaignDefinitions, snapshot.CampaignDefinitionViews.Count, errors);
        RequireNonEmpty(GameCatalogDatasets.Lres, snapshot.LreViews.Count, errors);
        RequireNonEmpty(GameCatalogDatasets.LreBattles, snapshot.LreBattleViews.Count, errors);
        RequireNonEmpty(GameCatalogDatasets.LreCommon, snapshot.LreCommonViews.Count, errors);

        // Every battle id referenced by a campaign definition must resolve to a served campaign battle.
        var battleIds = new HashSet<string>(
            snapshot.CampaignBattleViews.Select(battle => battle.Id), StringComparer.Ordinal);

        foreach (var definition in snapshot.CampaignDefinitionViews)
        {
            foreach (var battleId in definition.BattleIds)
            {
                RequireReference(
                    GameCatalogDatasets.CampaignDefinitions, definition.GroupId, "battleIds", battleId, battleIds, errors);
            }
        }

        // Every battle id referenced by an LRE track must resolve to a served LRE battle.
        var lreBattleIds = new HashSet<string>(
            snapshot.LreBattleViews.Select(battle => battle.Id), StringComparer.Ordinal);

        foreach (var lre in snapshot.LreViews)
        {
            foreach (var track in new[] { lre.Alpha, lre.Beta, lre.Gamma })
            {
                foreach (var battleId in track.BattleIds)
                {
                    RequireReference(GameCatalogDatasets.Lres, lre.Id, "battleIds", battleId, lreBattleIds, errors);
                }
            }
        }
    }
}
