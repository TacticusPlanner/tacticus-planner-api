namespace TacticusPlanner.Catalog;

public static class CatalogValidator
{
    public static IReadOnlyList<CatalogValidationError> Validate(CatalogSnapshot snapshot)
    {
        var errors = new List<CatalogValidationError>();

        ValidateManifestDatasets(snapshot, errors);
        ValidateUniqueIds(CatalogDatasets.Units, snapshot.Units, unit => unit.Id, errors);
        ValidateUniqueIds(CatalogDatasets.Mows, snapshot.Mows, mow => mow.Id, errors);
        ValidateUniqueIds(CatalogDatasets.Upgrades, snapshot.Upgrades, upgrade => upgrade.Id, errors);
        ValidateUniqueIds(CatalogDatasets.Equipment, snapshot.Equipment, item => item.Id, errors);
        ValidateUniqueIds(CatalogDatasets.Campaigns, snapshot.Campaigns, campaign => campaign.Id, errors);
        ValidateUniqueIds(CatalogDatasets.CampaignBattles, snapshot.CampaignBattles, battle => battle.Id, errors);
        ValidateUniqueIds(CatalogDatasets.Lres, snapshot.Lres, lre => lre.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), errors);
        ValidateRequiredFields(snapshot, errors);
        ValidateReferences(snapshot, errors);

        return errors;
    }

    private static void ValidateManifestDatasets(CatalogSnapshot snapshot, List<CatalogValidationError> errors)
    {
        foreach (var requiredDataset in CatalogDatasets.Required)
        {
            if (!snapshot.DatasetHashes.ContainsKey(requiredDataset))
            {
                errors.Add(new CatalogValidationError(requiredDataset, "MissingDataset", $"Dataset '{requiredDataset}' is missing."));
            }
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
        foreach (var unit in snapshot.Units)
        {
            Require(CatalogDatasets.Units, unit.Id, unit.Name, "name", errors);
            Require(CatalogDatasets.Units, unit.Id, unit.Faction, "faction", errors);
            Require(CatalogDatasets.Units, unit.Id, unit.Alliance, "alliance", errors);
        }

        foreach (var mow in snapshot.Mows)
        {
            Require(CatalogDatasets.Mows, mow.Id, mow.Name, "name", errors);
            Require(CatalogDatasets.Mows, mow.Id, mow.PrimaryAbility.Name, "primaryAbility.name", errors);
            Require(CatalogDatasets.Mows, mow.Id, mow.SecondaryAbility.Name, "secondaryAbility.name", errors);
        }

        foreach (var upgrade in snapshot.Upgrades)
        {
            Require(CatalogDatasets.Upgrades, upgrade.Id, upgrade.Material, "material", errors);
            Require(CatalogDatasets.Upgrades, upgrade.Id, upgrade.Rarity, "rarity", errors);
        }

        foreach (var campaign in snapshot.Campaigns)
        {
            Require(CatalogDatasets.Campaigns, campaign.Id, campaign.ReleaseType, "releaseType", errors);
            Require(CatalogDatasets.Campaigns, campaign.Id, campaign.Difficulty, "difficulty", errors);
        }
    }

    private static void ValidateReferences(CatalogSnapshot snapshot, List<CatalogValidationError> errors)
    {
        var unitIds = new HashSet<string>(snapshot.Units.Select(unit => unit.Id), StringComparer.OrdinalIgnoreCase);
        var mowIds = new HashSet<string>(snapshot.Mows.Select(mow => mow.Id), StringComparer.OrdinalIgnoreCase);
        var unitOrMowIds = new HashSet<string>(unitIds, StringComparer.OrdinalIgnoreCase);
        unitOrMowIds.UnionWith(mowIds);
        var upgradeIds = new HashSet<string>(snapshot.Upgrades.Select(upgrade => upgrade.Id), StringComparer.OrdinalIgnoreCase);
        var campaignIds = new HashSet<string>(snapshot.Campaigns.Select(campaign => campaign.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var mow in snapshot.Mows)
        {
            foreach (var upgradeId in mow.PrimaryAbility.Recipes.Concat(mow.SecondaryAbility.Recipes).SelectMany(recipe => recipe))
            {
                RequireReference(CatalogDatasets.Mows, mow.Id, "upgrade", upgradeId, upgradeIds, errors);
            }
        }

        foreach (var upgrade in snapshot.Upgrades)
        {
            foreach (var ingredient in upgrade.Recipe)
            {
                RequireReference(CatalogDatasets.Upgrades, upgrade.Id, "recipe.material", ingredient.Material, upgradeIds, errors);
            }
        }

        foreach (var item in snapshot.Equipment)
        {
            foreach (var unitId in item.AllowedUnits)
            {
                RequireReference(CatalogDatasets.Equipment, item.Id, "allowedUnits", unitId, unitIds, errors);
            }
        }

        foreach (var campaign in snapshot.Campaigns)
        {
            foreach (var unitId in campaign.CoreCharacters)
            {
                RequireReference(CatalogDatasets.Campaigns, campaign.Id, "coreCharacters", unitId, unitIds, errors);
            }
        }

        foreach (var battle in snapshot.CampaignBattles)
        {
            RequireReference(CatalogDatasets.CampaignBattles, battle.Id, "campaign", battle.Campaign, campaignIds, errors);

            foreach (var unitId in battle.RequiredCharacterSnowprintIds)
            {
                RequireReference(CatalogDatasets.CampaignBattles, battle.Id, "requiredCharacterSnowprintIds", unitId, unitIds, errors);
            }

            foreach (var reward in battle.Rewards.AllRewards)
            {
                ValidateRewardReference(battle.Id, reward.Id, upgradeIds, unitOrMowIds, errors);
            }
        }

        foreach (var lre in snapshot.Lres)
        {
            RequireReference(CatalogDatasets.Lres, lre.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), "unitSnowprintId", lre.UnitSnowprintId, unitIds, errors);
        }
    }

    private static void ValidateRewardReference(
        string ownerId,
        string rewardId,
        ISet<string> upgradeIds,
        ISet<string> unitOrMowIds,
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
                RequireReference(CatalogDatasets.CampaignBattles, ownerId, "reward", unitId, unitOrMowIds, errors);
                return;
            }
        }

        RequireReference(CatalogDatasets.CampaignBattles, ownerId, "reward", rewardId, upgradeIds, errors);
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
        ISet<string> validReferences,
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
