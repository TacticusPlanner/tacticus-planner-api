using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private static void ValidateReferences(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors)
    {
        var unitIds = new HashSet<string>(snapshot.Characters.Select(character => character.Id), StringComparer.OrdinalIgnoreCase);
        var mowIds = new HashSet<string>(snapshot.Mows.Select(mow => mow.Id), StringComparer.OrdinalIgnoreCase);
        var unitOrMowIds = new HashSet<string>(unitIds, StringComparer.OrdinalIgnoreCase);
        unitOrMowIds.UnionWith(mowIds);
        var upgradeIds = new HashSet<string>(snapshot.Upgrades.Select(upgrade => upgrade.Id), StringComparer.OrdinalIgnoreCase);
        var dropChanceIds = new HashSet<string>(snapshot.DropChances.Select(chance => chance.Id), StringComparer.OrdinalIgnoreCase);
        var npcIds = new HashSet<string>(snapshot.Npcs.Select(npc => npc.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var mow in snapshot.Mows)
        {
            foreach (var upgradeId in mow.PrimaryAbility.Recipes.Concat(mow.SecondaryAbility.Recipes).SelectMany(recipe => recipe))
            {
                RequireReference("mows", mow.Id, "upgrade", upgradeId, upgradeIds, errors);
            }
        }

        foreach (var upgrade in snapshot.Upgrades)
        {
            foreach (var ingredient in upgrade.Recipe)
            {
                RequireReference(GameCatalogDatasets.UpgradesPrefix, upgrade.Id, "recipe.material", ingredient.Material, upgradeIds, errors);
            }
        }

        foreach (var character in snapshot.Characters)
        {
            foreach (var rankUp in character.RankUpUpgrades)
            {
                foreach (var upgradeId in rankUp.UpgradeIds)
                {
                    RequireReference(GameCatalogDatasets.UnitsPrefix, character.Id, "rankUpUpgrades", upgradeId, upgradeIds, errors);
                }
            }
        }

        foreach (var item in snapshot.Equipment)
        {
            foreach (var unitId in item.AllowedUnits)
            {
                RequireReference(GameCatalogDatasets.EquipmentPrefix, item.Id, "allowedUnits", unitId, unitIds, errors);
            }
        }

        foreach (var (key, group) in snapshot.CampaignGroups)
        {
            foreach (var unitId in group.CoreCharacters)
            {
                RequireReference(GameCatalogDatasets.CampaignBattlesPrefix, key, "coreCharacters", unitId, unitIds, errors);
            }
        }

        foreach (var battle in snapshot.CampaignBattles)
        {
            foreach (var rewardId in battle.Rewards.AllRewardIds)
            {
                ValidateRewardReference(battle.Id, rewardId, upgradeIds, unitOrMowIds, errors);
            }

            foreach (var potential in battle.Rewards.Potential)
            {
                RequireReference(GameCatalogDatasets.CampaignBattlesPrefix, battle.Id, "potential.chanceId", potential.ChanceId, dropChanceIds, errors);
            }
        }

        foreach (var lre in snapshot.Lres)
        {
            var lreId = lre.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            RequireReference(GameCatalogDatasets.LresPrefix, lreId, "unitSnowprintId", lre.UnitSnowprintId, unitIds, errors);

            foreach (var track in new[] { lre.Alpha, lre.Beta, lre.Gamma })
            {
                foreach (var enemyId in track.Battles.SelectMany(battle => battle.Waves).SelectMany(wave => wave.Enemies).Select(enemy => enemy.Id))
                {
                    RequireReference(GameCatalogDatasets.LresPrefix, lreId, "battles.waves.enemies", enemyId, npcIds, errors);
                }
            }
        }
    }

    private static void ValidateRewardReference(
        string ownerId,
        string rewardId,
        HashSet<string> upgradeIds,
        HashSet<string> unitOrMowIds,
        List<GameCatalogValidationError> errors
    )
    {
        if (string.IsNullOrWhiteSpace(rewardId) || string.Equals(rewardId, "gold", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var shardPrefix in new[] { "shards_", "mythicShards_" })
        {
            if (rewardId.StartsWith(shardPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var unitId = rewardId[shardPrefix.Length..];
                RequireReference(GameCatalogDatasets.CampaignBattlesPrefix, ownerId, "reward", unitId, unitOrMowIds, errors);
                return;
            }
        }

        RequireReference(GameCatalogDatasets.CampaignBattlesPrefix, ownerId, "reward", rewardId, upgradeIds, errors);
    }
}
