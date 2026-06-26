using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private static void ValidateManifestDatasets(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors)
    {
        foreach (var servedDataset in GameCatalogDatasets.Served)
        {
            if (!snapshot.DatasetHashes.ContainsKey(servedDataset))
            {
                errors.Add(new GameCatalogValidationError(servedDataset, "MissingDataset", $"Served dataset '{servedDataset}' is missing."));
            }
        }
    }

    private static void ValidateServedProjections(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors)
    {
        RequireNonEmpty(GameCatalogDatasets.Characters, snapshot.CharacterViews.Count, errors);
        RequireNonEmpty(GameCatalogDatasets.Npcs, snapshot.NpcList.Count, errors);
        RequireNonEmpty(GameCatalogDatasets.Mows, snapshot.MowList.Count, errors);
        RequireNonEmpty(GameCatalogDatasets.MowUpgradeCostsServed, snapshot.MowUpgradeCostViews.Count, errors);
        RequireNonEmpty(GameCatalogDatasets.Upgrades, snapshot.UpgradeViews.Count, errors);
        RequireNonEmpty(GameCatalogDatasets.Equipment, snapshot.EquipmentViews.Count, errors);
        RequireNonEmpty(GameCatalogDatasets.CampaignBattles, snapshot.CampaignBattleViews.Count, errors);
        RequireNonEmpty(GameCatalogDatasets.CampaignDefinitions, snapshot.CampaignDefinitionViews.Count, errors);
        RequireNonEmpty(GameCatalogDatasets.Lres, snapshot.LreViews.Count, errors);

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
    }
}
