using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private static void ValidateEvents(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors) =>
        ValidateEvents(snapshot.EventDefinitions, snapshot.EventOccurrences, errors);

    /// <summary>
    /// Takes only the raw event collections (not the full snapshot) so it is directly unit testable without
    /// constructing an entire <see cref="GameCatalogSnapshot"/>, mirroring the denormalizer's pure-function shape.
    /// </summary>
    internal static void ValidateEvents(
        IReadOnlyList<GameCatalogEventDefinition> definitions,
        IReadOnlyList<GameCatalogEventOccurrence> occurrences,
        List<GameCatalogValidationError> errors)
    {
        var definitionsById = definitions.ToDictionary(definition => definition.Id, StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            var recurrence = definition.Recurrence;
            if (recurrence.Kind != "Fixed")
            {
                continue;
            }

            if (recurrence.IntervalDays is null || recurrence.DurationDays is null || recurrence.AnchorUtc is null)
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.EventDefinitions,
                    "RequiredField",
                    $"'{definition.Id}' has Fixed recurrence but is missing intervalDays, durationDays, or anchorUtc."));
                continue;
            }

            if (recurrence.DurationDays >= recurrence.IntervalDays)
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.EventDefinitions,
                    "InvalidRecurrence",
                    $"'{definition.Id}' has durationDays ({recurrence.DurationDays}) >= intervalDays ({recurrence.IntervalDays}); adjacent projected slots would overlap."));
            }
        }

        foreach (var occurrence in occurrences)
        {
            if (!definitionsById.TryGetValue(occurrence.DefinitionId, out var definition))
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.EventOccurrences,
                    "MissingReference",
                    $"'{occurrence.Id}' has unresolved definitionId reference '{occurrence.DefinitionId}'."));
                continue;
            }

            var suppliedParameters = occurrence.Parameters?.Keys ?? [];
            var missingParameters = definition.RequiredParameters.Where(required => !suppliedParameters.Contains(required));

            foreach (var missingParameter in missingParameters)
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.EventOccurrences,
                    "MissingRequiredParameter",
                    $"'{occurrence.Id}' is missing required parameter '{missingParameter}' declared by definition '{definition.Id}'."));
            }
        }
    }
}
