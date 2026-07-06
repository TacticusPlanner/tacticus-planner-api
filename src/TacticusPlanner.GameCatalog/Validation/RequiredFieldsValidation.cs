using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private static void ValidateRequiredFields(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors)
    {
        foreach (var (key, faction) in snapshot.UnitsByFaction)
        {
            Require(GameCatalogDatasets.UnitsPrefix, key, faction.FactionId, "factionId", errors);
            Require(GameCatalogDatasets.UnitsPrefix, key, faction.Name, "name", errors);
            Require(GameCatalogDatasets.UnitsPrefix, key, faction.Alliance, "alliance", errors);

            foreach (var character in faction.Characters)
            {
                Require(GameCatalogDatasets.UnitsPrefix, character.Id, character.Name, "name", errors);
            }
        }

        foreach (var (key, faction) in snapshot.NpcsByFaction)
        {
            Require(GameCatalogDatasets.NpcsPrefix, key, faction.FactionId, "factionId", errors);
            Require(GameCatalogDatasets.NpcsPrefix, key, faction.Name, "name", errors);

            foreach (var npc in faction.Npcs)
            {
                Require(GameCatalogDatasets.NpcsPrefix, npc.Id, npc.Name, "name", errors);
            }
        }

        foreach (var mow in snapshot.Mows)
        {
            Require("mows", mow.Id, mow.Name, "name", errors);
            Require("mows", mow.Id, mow.PrimaryAbility.Name, "primaryAbility.name", errors);
            Require("mows", mow.Id, mow.SecondaryAbility.Name, "secondaryAbility.name", errors);
        }

        foreach (var upgrade in snapshot.Upgrades)
        {
            Require(GameCatalogDatasets.UpgradesPrefix, upgrade.Id, upgrade.Material, "material", errors);
            Require(GameCatalogDatasets.UpgradesPrefix, upgrade.Id, upgrade.Rarity, "rarity", errors);
        }

        foreach (var (key, group) in snapshot.CampaignGroups)
        {
            Require(GameCatalogDatasets.CampaignBattlesPrefix, key, group.Faction, "faction", errors);
            Require(GameCatalogDatasets.CampaignBattlesPrefix, key, group.ReleaseType, "releaseType", errors);

            if (group.Types.Count == 0)
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.CampaignBattlesPrefix, "RequiredField", $"'types' is required for '{key}'."));
            }
        }
    }
}
