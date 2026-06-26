namespace TacticusPlanner.Catalog;

public static class CatalogValidator
{
    public static IReadOnlyList<CatalogValidationError> Validate(CatalogSnapshot snapshot)
    {
        var errors = new List<CatalogValidationError>();

        ValidateManifestDatasets(snapshot, errors);
        ValidateUniqueIds(CatalogDatasets.UnitsPrefix, snapshot.Characters, character => character.Id, errors);
        ValidateUniqueIds("mows", snapshot.Mows, mow => mow.Id, errors);
        ValidateUniqueIds(CatalogDatasets.NpcsPrefix, snapshot.Npcs, npc => npc.Id, errors);
        ValidateUniqueIds(CatalogDatasets.UpgradesPrefix, snapshot.Upgrades, upgrade => upgrade.Id, errors);
        ValidateUniqueIds(CatalogDatasets.EquipmentPrefix, snapshot.Equipment, item => item.Id, errors);
        ValidateUniqueIds(CatalogDatasets.DropChances, snapshot.DropChances, chance => chance.Id, errors);
        ValidateUniqueIds(CatalogDatasets.CampaignBattlesPrefix, snapshot.CampaignBattles, battle => battle.Id, errors);
        ValidateUniqueIds(CatalogDatasets.LresPrefix, snapshot.Lres, lre => lre.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), errors);
        ValidateRequiredFields(snapshot, errors);
        ValidateReferences(snapshot, errors);
        ValidateServedProjections(snapshot, errors);

        return errors;
    }

    private static void ValidateManifestDatasets(CatalogSnapshot snapshot, List<CatalogValidationError> errors)
    {
        foreach (var servedDataset in CatalogDatasets.Served)
        {
            if (!snapshot.DatasetHashes.ContainsKey(servedDataset))
            {
                errors.Add(new CatalogValidationError(servedDataset, "MissingDataset", $"Served dataset '{servedDataset}' is missing."));
            }
        }
    }

    private static void ValidateServedProjections(CatalogSnapshot snapshot, List<CatalogValidationError> errors)
    {
        RequireNonEmpty(CatalogDatasets.Characters, snapshot.CharacterViews.Count, errors);
        RequireNonEmpty(CatalogDatasets.Npcs, snapshot.NpcList.Count, errors);
        RequireNonEmpty(CatalogDatasets.Mows, snapshot.MowDataset.Items.Count, errors);
        RequireNonEmpty(CatalogDatasets.Upgrades, snapshot.UpgradeViews.Count, errors);
        RequireNonEmpty(CatalogDatasets.Equipment, snapshot.EquipmentDataset.Items.Count, errors);
        RequireNonEmpty(CatalogDatasets.CampaignBattles, snapshot.CampaignGroupViews.Count, errors);
        RequireNonEmpty(CatalogDatasets.Lres, snapshot.LreViews.Count, errors);
    }

    private static void RequireNonEmpty(string dataset, int count, List<CatalogValidationError> errors)
    {
        if (count == 0)
        {
            errors.Add(new CatalogValidationError(dataset, "EmptyDataset", $"Served dataset '{dataset}' is empty."));
        }
    }

    private static void ValidateUniqueIds<T>(
        string dataset,
        IEnumerable<T> values,
        Func<T, string> keySelector,
        List<CatalogValidationError> errors
    )
    {
        var duplicates = values
            .Select(keySelector)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);

        foreach (var duplicate in duplicates)
        {
            errors.Add(new CatalogValidationError(dataset, "DuplicateId", $"Duplicate id '{duplicate.Key}' in dataset '{dataset}'."));
        }
    }

    private static void ValidateRequiredFields(CatalogSnapshot snapshot, List<CatalogValidationError> errors)
    {
        foreach (var (key, faction) in snapshot.UnitsByFaction)
        {
            Require(CatalogDatasets.UnitsPrefix, key, faction.FactionId, "factionId", errors);
            Require(CatalogDatasets.UnitsPrefix, key, faction.Name, "name", errors);
            Require(CatalogDatasets.UnitsPrefix, key, faction.Alliance, "alliance", errors);

            foreach (var character in faction.Characters)
            {
                Require(CatalogDatasets.UnitsPrefix, character.Id, character.Name, "name", errors);
            }
        }

        foreach (var (key, faction) in snapshot.NpcsByFaction)
        {
            Require(CatalogDatasets.NpcsPrefix, key, faction.FactionId, "factionId", errors);
            Require(CatalogDatasets.NpcsPrefix, key, faction.Name, "name", errors);

            foreach (var npc in faction.Npcs)
            {
                Require(CatalogDatasets.NpcsPrefix, npc.Id, npc.Name, "name", errors);
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
            Require(CatalogDatasets.UpgradesPrefix, upgrade.Id, upgrade.Material, "material", errors);
            Require(CatalogDatasets.UpgradesPrefix, upgrade.Id, upgrade.Rarity, "rarity", errors);
        }

        foreach (var (key, group) in snapshot.CampaignGroups)
        {
            Require(CatalogDatasets.CampaignBattlesPrefix, key, group.Faction, "faction", errors);
            Require(CatalogDatasets.CampaignBattlesPrefix, key, group.ReleaseType, "releaseType", errors);

            if (group.Difficulties.Count == 0)
            {
                errors.Add(new CatalogValidationError(
                    CatalogDatasets.CampaignBattlesPrefix, "RequiredField", $"'difficulties' is required for '{key}'."));
            }
        }
    }

    private static void ValidateReferences(CatalogSnapshot snapshot, List<CatalogValidationError> errors)
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
                RequireReference(CatalogDatasets.UpgradesPrefix, upgrade.Id, "recipe.material", ingredient.Material, upgradeIds, errors);
            }
        }

        foreach (var character in snapshot.Characters)
        {
            foreach (var rankUp in character.RankUpUpgrades)
            {
                foreach (var upgradeId in rankUp.UpgradeIds)
                {
                    RequireReference(CatalogDatasets.UnitsPrefix, character.Id, "rankUpUpgrades", upgradeId, upgradeIds, errors);
                }
            }
        }

        foreach (var item in snapshot.Equipment)
        {
            foreach (var unitId in item.AllowedUnits)
            {
                RequireReference(CatalogDatasets.EquipmentPrefix, item.Id, "allowedUnits", unitId, unitIds, errors);
            }
        }

        foreach (var (key, group) in snapshot.CampaignGroups)
        {
            foreach (var unitId in group.CoreCharacters)
            {
                RequireReference(CatalogDatasets.CampaignBattlesPrefix, key, "coreCharacters", unitId, unitIds, errors);
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
                RequireReference(CatalogDatasets.CampaignBattlesPrefix, battle.Id, "potential.chanceId", potential.ChanceId, dropChanceIds, errors);
            }
        }

        foreach (var lre in snapshot.Lres)
        {
            var lreId = lre.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            RequireReference(CatalogDatasets.LresPrefix, lreId, "unitSnowprintId", lre.UnitSnowprintId, unitIds, errors);

            foreach (var track in new[] { lre.Alpha, lre.Beta, lre.Gamma })
            {
                foreach (var enemyId in track.Battles.SelectMany(battle => battle.Waves).SelectMany(wave => wave.Enemies).Select(enemy => enemy.Id))
                {
                    RequireReference(CatalogDatasets.LresPrefix, lreId, "battles.waves.enemies", enemyId, npcIds, errors);
                }
            }
        }
    }

    private static void ValidateRewardReference(
        string ownerId,
        string rewardId,
        HashSet<string> upgradeIds,
        HashSet<string> unitOrMowIds,
        List<CatalogValidationError> errors
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
                RequireReference(CatalogDatasets.CampaignBattlesPrefix, ownerId, "reward", unitId, unitOrMowIds, errors);
                return;
            }
        }

        RequireReference(CatalogDatasets.CampaignBattlesPrefix, ownerId, "reward", rewardId, upgradeIds, errors);
    }

    private static void Require(
        string dataset,
        string ownerId,
        string? value,
        string field,
        List<CatalogValidationError> errors
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new CatalogValidationError(dataset, "RequiredField", $"'{field}' is required for '{ownerId}'."));
        }
    }

    private static void RequireReference(
        string dataset,
        string ownerId,
        string field,
        string reference,
        HashSet<string> validReferences,
        List<CatalogValidationError> errors
    )
    {
        if (string.IsNullOrWhiteSpace(reference) || validReferences.Contains(reference))
        {
            return;
        }

        errors.Add(new CatalogValidationError(dataset, "MissingReference", $"'{ownerId}' has unresolved {field} reference '{reference}'."));
    }
}

public sealed record CatalogValidationError(
    string Dataset,
    string Code,
    string Message
);
