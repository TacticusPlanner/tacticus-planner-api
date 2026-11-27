using System.Text.Json;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private static void ValidateSurvivalEvents(GameCatalogCompilation snapshot, List<GameCatalogValidationError> errors) =>
        ValidateSurvivalEvents(
            snapshot.SurvivalEventViews,
            [.. snapshot.CharacterViews.Select(character => character.Id)],
            [.. snapshot.NpcList.Select(npc => npc.Id)],
            snapshot.EventDefinitions,
            snapshot.EventOccurrences,
            errors);

    /// <summary>
    /// Each Survival event's featured hero is a served character, every enemy, summon and kill score names a served
    /// npc, and it allows at least one faction. Each <c>survival</c> calendar occurrence names a served Survival event,
    /// and no event is dated twice. Failures are <c>InvalidSurvivalEvent</c> (dataset) and
    /// <c>UnknownSurvivalEvent</c> / <c>DuplicateSurvivalOccurrence</c> (calendar) errors.
    /// </summary>
    internal static void ValidateSurvivalEvents(
        IReadOnlyList<GameCatalogSurvivalEvent> survivalEvents,
        IReadOnlyCollection<string> characterIds,
        IReadOnlyCollection<string> npcIds,
        IReadOnlyList<GameCatalogEventDefinition> definitions,
        IReadOnlyList<GameCatalogEventOccurrence> occurrences,
        List<GameCatalogValidationError> errors)
    {
        ValidateUniqueIds(GameCatalogDatasets.SurvivalEvents, survivalEvents, survivalEvent => survivalEvent.Id, errors);
        var characters = characterIds.ToHashSet(StringComparer.Ordinal);
        var npcs = npcIds.ToHashSet(StringComparer.Ordinal);

        void Fail(GameCatalogSurvivalEvent survivalEvent, string message) =>
            errors.Add(new GameCatalogValidationError(
                GameCatalogDatasets.SurvivalEvents, "InvalidSurvivalEvent", $"Survival event '{survivalEvent.Id}' {message}"));

        foreach (var survivalEvent in survivalEvents)
        {
            if (!characters.Contains(survivalEvent.FeaturedCharacterId))
            {
                Fail(survivalEvent, $"features '{survivalEvent.FeaturedCharacterId}', which is not a served character.");
            }

            if (survivalEvent.Rules.AllowedFactionIds.Count == 0)
            {
                Fail(survivalEvent, "allows no faction.");
            }

            var named = survivalEvent.Waves
                .SelectMany(wave => wave.Enemies.Select(enemy => enemy.NpcId)
                    .Concat(wave.PowerUpsOnClear.Select(powerUp => powerUp.NpcId).OfType<string>()))
                .Concat(survivalEvent.Scoring.KillScores.Select(score => score.NpcId))
                .Distinct(StringComparer.Ordinal);
            foreach (var npcId in named.Where(npcId => !npcs.Contains(npcId)))
            {
                Fail(survivalEvent, $"names '{npcId}', which is not a served npc.");
            }
        }

        var survivalDefinitions = definitions
            .Where(definition => definition.Type == GameCatalogDenormalizer.SurvivalEventType)
            .Select(definition => definition.Id)
            .ToHashSet(StringComparer.Ordinal);
        var servedIds = survivalEvents.Select(survivalEvent => survivalEvent.Id).ToHashSet(StringComparer.Ordinal);
        var dated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var occurrence in occurrences.Where(occurrence => survivalDefinitions.Contains(occurrence.DefinitionId)))
        {
            if (occurrence.Parameters?.TryGetValue(GameCatalogDenormalizer.SurvivalEventIdParameter, out var id) != true)
            {
                continue; // reported as MissingRequiredParameter
            }

            var survivalEventId = id.ValueKind == JsonValueKind.String ? id.GetString()! : string.Empty;
            if (!servedIds.Contains(survivalEventId))
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.EventOccurrences,
                    "UnknownSurvivalEvent",
                    $"'{occurrence.Id}' has {GameCatalogDenormalizer.SurvivalEventIdParameter} '{id}'; expected a served survival-events id."));
            }
            else if (!dated.Add(survivalEventId))
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.EventOccurrences,
                    "DuplicateSurvivalOccurrence",
                    $"'{occurrence.Id}' dates Survival event '{survivalEventId}' a second time."));
            }
        }
    }
}
