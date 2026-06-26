using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

/// <summary>
/// Validates a loaded <see cref="GameCatalogSnapshot"/> at startup (fail-fast): every served dataset is
/// present and non-empty, ids are unique, required fields are set, and cross-dataset references resolve.
/// The checks are split by concern across the <c>Validation</c> folder; this file is the orchestrator and
/// the shared assertion helpers.
/// </summary>
public static partial class GameCatalogValidator
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
