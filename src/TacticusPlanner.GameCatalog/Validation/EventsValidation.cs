using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private static readonly HashSet<string> ValidRecurrenceKinds = new(StringComparer.Ordinal) { "Fixed", "None" };

    /// <summary>The <c>guild-war-season</c> occurrence parameter naming the datamine season config that season runs.</summary>
    public const string GuildWarSeasonConfigId = "seasonConfigId";

    private static void ValidateEvents(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors)
    {
        ValidateEvents(snapshot.EventDefinitions, snapshot.EventOccurrences, errors);
        ValidateGuildWarSeasonConfigs(
            snapshot.EventDefinitions,
            snapshot.EventOccurrences,
            [.. snapshot.GuildWarViews.Select(season => season.Id)],
            errors);
    }

    /// <summary>
    /// A <c>GuildWarSeason</c> occurrence may name the datamine season config it runs (<c>seasonConfigId</c>); when it
    /// does, that config must be a served guild-war season, so clients can default a new war to it.
    /// </summary>
    internal static void ValidateGuildWarSeasonConfigs(
        IReadOnlyList<GameCatalogEventDefinition> definitions,
        IReadOnlyList<GameCatalogEventOccurrence> occurrences,
        IReadOnlyCollection<string> seasonIds,
        List<GameCatalogValidationError> errors)
    {
        var guildWarDefinitions = definitions
            .Where(definition => definition.Type == "GuildWarSeason")
            .Select(definition => definition.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var occurrence in occurrences.Where(occurrence => guildWarDefinitions.Contains(occurrence.DefinitionId)))
        {
            if (occurrence.Parameters?.TryGetValue(GuildWarSeasonConfigId, out var configId) != true)
            {
                continue;
            }

            if (configId.ValueKind != System.Text.Json.JsonValueKind.String
                || !seasonIds.Contains(configId.GetString()!, StringComparer.Ordinal))
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.EventOccurrences,
                    "UnknownGuildWarSeasonConfig",
                    $"'{occurrence.Id}' has {GuildWarSeasonConfigId} '{configId}'; expected a served guild-war season."));
            }
        }
    }

    /// <summary>
    /// Takes only the raw event collections (not the full snapshot) so it is directly unit testable without
    /// constructing an entire <see cref="GameCatalogSnapshot"/>, mirroring the denormalizer's pure-function shape.
    /// </summary>
    internal static void ValidateEvents(
        IReadOnlyList<GameCatalogEventDefinition> definitions,
        IReadOnlyList<GameCatalogEventOccurrence> occurrences,
        List<GameCatalogValidationError> errors)
    {
        // GroupBy+First (not ToDictionary directly) so a duplicate definition id — already reported by
        // ValidateUniqueIds — doesn't also throw here and mask that cleaner error with a raw exception.
        var definitionsById = definitions
            .GroupBy(definition => definition.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            var recurrence = definition.Recurrence;

            if (!ValidRecurrenceKinds.Contains(recurrence.Kind))
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.EventDefinitions,
                    "InvalidRecurrenceKind",
                    $"'{definition.Id}' has unrecognized recurrence kind '{recurrence.Kind}'; expected 'Fixed' or 'None'."));
                continue;
            }

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

            if (recurrence.IntervalDays <= 0 || recurrence.DurationDays <= 0)
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.EventDefinitions,
                    "InvalidRecurrence",
                    $"'{definition.Id}' has non-positive intervalDays ({recurrence.IntervalDays}) or durationDays ({recurrence.DurationDays})."));
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
            if (occurrence.StartUtc >= occurrence.EndUtc)
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.EventOccurrences,
                    "InvalidTimeWindow",
                    $"'{occurrence.Id}' has startUtc ({occurrence.StartUtc:O}) >= endUtc ({occurrence.EndUtc:O})."));
            }

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

            if (definition.Type == ShopEventParameters.DefinitionType)
            {
                ValidateShopEventParameters(occurrence, errors);
            }
        }
    }

    /// <summary>
    /// A <c>ShopEvent</c> week names its datamine event key (<c>October2026</c>) and may carry the week's estimated
    /// event-currency income. See specs/game-events-calendar-dataset and specs/game-event-shops-dataset.
    /// </summary>
    private static void ValidateShopEventParameters(
        GameCatalogEventOccurrence occurrence, List<GameCatalogValidationError> errors)
    {
        var parameters = occurrence.Parameters;
        if (parameters is null)
        {
            return;
        }

        // The event-shops denormalizer drops a week it cannot read, so a malformed final week would otherwise pass the
        // 1..N week check and silently shorten the event.
        if (parameters.TryGetValue(ShopEventParameters.WeekNumber, out var weekNumber)
            && ShopEventParameters.ReadWeekNumber(weekNumber) is null)
        {
            errors.Add(new GameCatalogValidationError(
                GameCatalogDatasets.EventOccurrences,
                "InvalidShopEventWeek",
                $"'{occurrence.Id}' has weekNumber '{weekNumber}'; expected a positive integer."));
        }

        if (parameters.TryGetValue(ShopEventParameters.EventKey, out var eventKey)
            && ShopEventParameters.ReadEventKey(eventKey) is null)
        {
            errors.Add(new GameCatalogValidationError(
                GameCatalogDatasets.EventOccurrences,
                "InvalidShopEventKey",
                $"'{occurrence.Id}' has eventKey '{eventKey}'; expected a datamine event key like 'October2026'."));
        }

        foreach (var incomeParameter in ShopEventParameters.IncomeParameters)
        {
            if (parameters.TryGetValue(incomeParameter, out var income)
                && ShopEventParameters.ReadIncome(income) is null)
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.EventOccurrences,
                    "InvalidShopEventIncome",
                    $"'{occurrence.Id}' has {incomeParameter} '{income}'; expected a non-negative integer."));
            }
        }
    }
}
