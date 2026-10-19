using System.Globalization;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    /// <summary>The horde-mode tiers, best first; the file lists exactly these, in this order.</summary>
    internal static readonly IReadOnlyList<string> HordeModeTierNames = ["S", "A", "B", "C", "D"];

    private const double MaxHordeModeMultiplier = 5;

    private static void ValidateHordeModeTiers(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors)
    {
        var characterIds = new HashSet<string>(snapshot.Characters.Select(character => character.Id), StringComparer.Ordinal);

        ValidateHordeModeTiers(snapshot.HordeModeTiersViews, characterIds, errors);
    }

    /// <summary>
    /// Validates the <c>horde-mode-tiers</c> file: exactly one record with the dataset's id; <c>tiers</c>
    /// lists S, A, B, C and D in that order, each with a <c>multiplier</c> in (0, 5]; every character's
    /// <c>unitId</c> is a catalog character id (not a MoW or NPC) listed once, with one of those tiers and,
    /// when set, its own <c>multiplier</c> override in (0, 5].
    /// Each failure is an <c>InvalidHordeModeTiers</c> error naming the offending value.
    /// </summary>
    internal static void ValidateHordeModeTiers(
        IReadOnlyList<GameCatalogHordeModeTiers> records,
        HashSet<string> characterIds,
        List<GameCatalogValidationError> errors)
    {
        const string dataset = GameCatalogDatasets.HordeModeTiers;
        const string code = "InvalidHordeModeTiers";

        if (records.Count != 1)
        {
            errors.Add(new GameCatalogValidationError(
                dataset, code, $"Dataset '{dataset}' must contain exactly one record, found {records.Count}."));
            return;
        }

        var record = records[0];

        if (record.Id != dataset)
        {
            errors.Add(new GameCatalogValidationError(
                dataset, code, $"Dataset '{dataset}' record id must be '{dataset}', found '{record.Id}'."));
        }

        var tiers = record.Tiers ?? [];
        var tierNames = tiers.Select(tier => tier.Tier).ToArray();
        if (!tierNames.SequenceEqual(HordeModeTierNames, StringComparer.Ordinal))
        {
            errors.Add(new GameCatalogValidationError(
                dataset,
                code,
                $"Dataset '{dataset}' tiers '{string.Join(", ", tierNames)}' must be '{string.Join(", ", HordeModeTierNames)}' in that order."));
        }

        foreach (var tier in tiers.Where(tier => !IsHordeModeMultiplier(tier.Multiplier)))
        {
            errors.Add(new GameCatalogValidationError(
                dataset,
                code,
                $"Dataset '{dataset}' tier '{tier.Tier}' multiplier '{FormatCatalogNumber(tier.Multiplier)}' must be in (0, {FormatCatalogNumber(MaxHordeModeMultiplier)}]."));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var character in record.Characters ?? [])
        {
            if (string.IsNullOrWhiteSpace(character.UnitId) || !characterIds.Contains(character.UnitId))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, code, $"Dataset '{dataset}' unitId '{character.UnitId}' is not a catalog character id."));
            }
            else if (!seen.Add(character.UnitId))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, code, $"Dataset '{dataset}' unitId '{character.UnitId}' is listed more than once."));
            }

            if (!HordeModeTierNames.Contains(character.Tier, StringComparer.Ordinal))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset,
                    code,
                    $"Dataset '{dataset}' unitId '{character.UnitId}' tier '{character.Tier}' must be one of {string.Join(", ", HordeModeTierNames)}."));
            }

            if (character.Multiplier is { } multiplier && !IsHordeModeMultiplier(multiplier))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset,
                    code,
                    $"Dataset '{dataset}' unitId '{character.UnitId}' multiplier '{FormatCatalogNumber(multiplier)}' must be in (0, {FormatCatalogNumber(MaxHordeModeMultiplier)}]."));
            }
        }
    }

    private static bool IsHordeModeMultiplier(double multiplier) =>
        multiplier > 0 && multiplier <= MaxHordeModeMultiplier;

    private static string FormatCatalogNumber(double value) => value.ToString(CultureInfo.InvariantCulture);
}
