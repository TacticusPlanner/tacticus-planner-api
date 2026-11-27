using System.Text.Json;
using System.Text.Json.Nodes;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    private const string HseConfigKey = "hse";

    // Config is served verbatim as a JsonElement, so the merged hse object is serialized here with the same
    // web (camelCase) conventions the catalog endpoints use for typed payloads.
    private static readonly JsonSerializerOptions HseJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// <c>event-definitions</c> is served as authored, except that a definition with an entry in the raw
    /// <c>home-screen-event-scoring</c> file gets that scoring merged into its <c>config</c> as <c>hse</c>
    /// (existing config keys are kept). See specs/home-screen-event-scoring.
    /// </summary>
    public static IReadOnlyList<GameCatalogEventDefinition> BuildEventDefinitions(
        IReadOnlyList<GameCatalogEventDefinition> definitions,
        IReadOnlyList<GameCatalogHseScoring> hseScoring)
    {
        var scoringByDefinition = hseScoring
            .GroupBy(scoring => scoring.DefinitionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        return definitions
            .Select(definition => scoringByDefinition.TryGetValue(definition.Id, out var scoring)
                ? definition with { Config = MergeHseConfig(definition.Config, scoring) }
                : definition)
            .ToArray();
    }

    private static JsonElement MergeHseConfig(JsonElement? config, GameCatalogHseScoring scoring)
    {
        var merged = config is { ValueKind: JsonValueKind.Object } existing
            ? JsonNode.Parse(existing.GetRawText())!.AsObject()
            : new JsonObject();
        merged[HseConfigKey] = JsonSerializer.SerializeToNode(new GameCatalogHseConfig(scoring.Variants), HseJsonOptions);

        return JsonSerializer.SerializeToElement(merged, HseJsonOptions);
    }

    /// <summary>Builds the served <c>events-calendar</c> (see <see cref="GameCatalogEventsCalendar.Build"/>; the
    /// runtime projects it too, once per UTC day).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<GameCatalogEventsCalendarEntry>> BuildEventsCalendar(
        IReadOnlyList<GameCatalogEventDefinition> definitions,
        IReadOnlyList<GameCatalogEventOccurrence> occurrences,
        DateTimeOffset now) =>
        GameCatalogEventsCalendar.Build(definitions, occurrences, now);
}
