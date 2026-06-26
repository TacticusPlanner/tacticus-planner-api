namespace TacticusPlanner.GameCatalog;

public static class GameCatalogValidator
{
    public static IReadOnlyList<GameCatalogValidationError> Validate(GameCatalogSnapshot snapshot)
    {
        var errors = new List<GameCatalogValidationError>();

        ValidateManifestDatasets(snapshot, errors);
        ValidateUniqueIds(GameCatalogDatasets.UnitsPrefix, snapshot.Characters, character => character.Id, errors);
        ValidateUniqueIds("mows", snapshot.Mows, mow => mow.Id, errors);
        ValidateUniqueIds(GameCatalogDatasets.NpcsPrefix, snapshot.Npcs, npc => npc.Id, errors);
        ValidateUniqueIds(GameCatalogDatasets.UpgradesPrefix, snapshot.Upgrades, upgrade => upgrade.Id, errors);
        ValidateUniqueIds(GameCatalogDatasets.EquipmentPrefix, snapshot.Equipment, item => item.Id, errors);
        ValidateUniqueIds(GameCatalogDatasets.DropChances, snapshot.DropChances, chance => chance.Id, errors);
        ValidateUniqueIds(GameCatalogDatasets.CampaignBattlesPrefix, snapshot.CampaignBattles, battle => battle.Id, errors);
        ValidateUniqueIds(GameCatalogDatasets.LresPrefix, snapshot.Lres, lre => lre.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), errors);
        ValidateRequiredFields(snapshot, errors);
        ValidateReferences(snapshot, errors);
        ValidateServedProjections(snapshot, errors);

        return errors;
    }

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
        RequireNonEmpty(GameCatalogDatasets.MowUpgradeCostsServed, snapshot.MowUpgradeCosts.Count, errors);
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

    private static void RequireNonEmpty(string dataset, int count, List<GameCatalogValidationError> errors)
    {
        if (count == 0)
        {
            errors.Add(new GameCatalogValidationError(dataset, "EmptyDataset", $"Served dataset '{dataset}' is empty."));
        }
    }

    private static void ValidateUniqueIds<T>(
        string dataset,
        IEnumerable<T> values,
        Func<T, string> keySelector,
        List<GameCatalogValidationError> errors
    )
    {
        var duplicates = values
            .Select(keySelector)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);

        foreach (var duplicate in duplicates)
        {
            errors.Add(new GameCatalogValidationError(dataset, "DuplicateId", $"Duplicate id '{duplicate.Key}' in dataset '{dataset}'."));
        }
    }

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

            if (group.Difficulties.Count == 0)
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.CampaignBattlesPrefix, "RequiredField", $"'difficulties' is required for '{key}'."));
            }
        }
    }

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

    private static void Require(
        string dataset,
        string ownerId,
        string? value,
        string field,
        List<GameCatalogValidationError> errors
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new GameCatalogValidationError(dataset, "RequiredField", $"'{field}' is required for '{ownerId}'."));
        }
    }

    private static void RequireReference(
        string dataset,
        string ownerId,
        string field,
        string reference,
        HashSet<string> validReferences,
        List<GameCatalogValidationError> errors
    )
    {
        if (string.IsNullOrWhiteSpace(reference) || validReferences.Contains(reference))
        {
            return;
        }

        errors.Add(new GameCatalogValidationError(dataset, "MissingReference", $"'{ownerId}' has unresolved {field} reference '{reference}'."));
    }
}

public sealed record GameCatalogValidationError(
    string Dataset,
    string Code,
    string Message
);
