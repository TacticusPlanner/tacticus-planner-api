using System.Text.Json;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private const string HomeScreenEventType = "HomeScreenEvent";
    private const string HseVariantParameter = "hseVariant";
    private const string TargetFactionParameter = "targetFactionId";
    private const string DefaultHseVariant = "default";

    private static readonly HashSet<string> ValidHseScopes = new(StringComparer.Ordinal) { "Personal", "Global" };
    private static readonly HashSet<string> ValidHseTiers = new(StringComparer.Ordinal) { "High", "Mid", "Low", "Default" };

    private static readonly HashSet<string> ValidHseTrackerKinds = new(StringComparer.Ordinal)
    {
        "KillUnits",
        "DefeatWaves",
        "DeployedUnitsOfFactionMajority",
        "FullLineUpOfFaction",
        "DonateUpgradeToGuildMember",
        "DonateIntelToGuildProject",
        "DispatchExpedition",
    };

    private static void ValidateHseScoring(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors) =>
        ValidateHseScoring(snapshot.EventDefinitions, snapshot.EventOccurrences, snapshot.HseScoring, errors);

    /// <summary>
    /// Checks the raw HSE scoring against its definitions, and every HSE occurrence's variant resolution
    /// (<c>hseVariant</c>, else <c>targetFactionId</c>, else <c>default</c>). See specs/home-screen-event-scoring.
    /// Takes only the raw collections so it is unit testable without a full snapshot, like <see cref="ValidateEvents(IReadOnlyList{GameCatalogEventDefinition}, IReadOnlyList{GameCatalogEventOccurrence}, List{GameCatalogValidationError})"/>.
    /// </summary>
    internal static void ValidateHseScoring(
        IReadOnlyList<GameCatalogEventDefinition> definitions,
        IReadOnlyList<GameCatalogEventOccurrence> occurrences,
        IReadOnlyList<GameCatalogHseScoring> scoring,
        List<GameCatalogValidationError> errors)
    {
        const string dataset = GameCatalogDatasets.HomeScreenEventScoring;

        var definitionsById = definitions
            .GroupBy(definition => definition.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        ValidateUniqueIds(dataset, scoring, entry => entry.DefinitionId, errors);

        foreach (var entry in scoring)
        {
            if (!definitionsById.TryGetValue(entry.DefinitionId, out var definition))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, "MissingReference", $"Scoring references unknown definition '{entry.DefinitionId}'."));
                continue;
            }

            if (definition.Type != HomeScreenEventType)
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, "InvalidReference", $"Scoring references '{entry.DefinitionId}', which is a '{definition.Type}', not a Home Screen Event."));
            }

            if (entry.Variants.Count == 0)
            {
                errors.Add(new GameCatalogValidationError(dataset, "RequiredField", $"'{entry.DefinitionId}' has no variants."));
            }

            ValidateUniqueIds(dataset, entry.Variants, variant => $"{entry.DefinitionId}/{variant.VariantKey}", errors);

            foreach (var variant in entry.Variants)
            {
                ValidateHseVariant($"{entry.DefinitionId}/{variant.VariantKey}", variant, errors);
            }
        }

        var variantKeysByDefinition = scoring
            .GroupBy(entry => entry.DefinitionId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().Variants.Select(variant => variant.VariantKey).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        foreach (var occurrence in occurrences)
        {
            var parameters = occurrence.Parameters;
            var variantKeys = variantKeysByDefinition.GetValueOrDefault(occurrence.DefinitionId);

            if (parameters is not null && parameters.TryGetValue(HseVariantParameter, out var hseVariant))
            {
                var key = hseVariant.ValueKind == JsonValueKind.String ? hseVariant.GetString() : null;
                if (key is null || variantKeys is null || !variantKeys.Contains(key))
                {
                    errors.Add(new GameCatalogValidationError(
                        GameCatalogDatasets.EventOccurrences,
                        "InvalidHseVariant",
                        $"'{occurrence.Id}' has hseVariant '{hseVariant}', which is not a variant of '{occurrence.DefinitionId}'."));
                }

                continue;
            }

            if (variantKeys is null)
            {
                continue;
            }

            var targetFaction = parameters is not null
                && parameters.TryGetValue(TargetFactionParameter, out var faction)
                && faction.ValueKind == JsonValueKind.String
                    ? faction.GetString()
                    : null;

            if (!(targetFaction is not null && variantKeys.Contains(targetFaction)) && !variantKeys.Contains(DefaultHseVariant))
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.EventOccurrences,
                    "UnresolvedHseVariant",
                    $"'{occurrence.Id}' resolves to no scoring variant of '{occurrence.DefinitionId}' (no hseVariant, no variant for its target faction, and no 'default')."));
            }
        }
    }

    private static void ValidateHseVariant(string label, GameCatalogHseVariant variant, List<GameCatalogValidationError> errors)
    {
        const string dataset = GameCatalogDatasets.HomeScreenEventScoring;

        if (!ValidHseScopes.Contains(variant.Scope))
        {
            errors.Add(new GameCatalogValidationError(dataset, "InvalidValue", $"'{label}' has unrecognized scope '{variant.Scope}'."));
        }

        if (variant.Tiers.Count == 0)
        {
            errors.Add(new GameCatalogValidationError(dataset, "RequiredField", $"'{label}' has no tiers."));
        }

        foreach (var tier in variant.Tiers)
        {
            var tierLabel = $"{label}/{tier.Tier}";

            if (!ValidHseTiers.Contains(tier.Tier))
            {
                errors.Add(new GameCatalogValidationError(dataset, "InvalidValue", $"'{tierLabel}' is not a recognized tier."));
            }

            if (tier.Milestones.Count == 0)
            {
                errors.Add(new GameCatalogValidationError(dataset, "RequiredField", $"'{tierLabel}' has no milestones."));
            }

            var previous = 0;
            foreach (var milestone in tier.Milestones)
            {
                if (milestone.RequiredPoints <= previous)
                {
                    errors.Add(new GameCatalogValidationError(
                        dataset, "InvalidLadder", $"'{tierLabel}' milestones are not strictly increasing ({milestone.RequiredPoints} after {previous})."));
                }

                if (milestone.Quantity <= 0)
                {
                    errors.Add(new GameCatalogValidationError(dataset, "InvalidValue", $"'{tierLabel}' has a milestone with quantity {milestone.Quantity}."));
                }

                previous = milestone.RequiredPoints;
            }

            if (tier.Repeat is { } repeat && (repeat.Points <= 0 || repeat.Quantity <= 0 || repeat.Cap is <= 0))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, "InvalidLadder", $"'{tierLabel}' has a repeat step with non-positive points, quantity or cap."));
            }
        }

        foreach (var tracker in variant.Trackers)
        {
            if (!ValidHseTrackerKinds.Contains(tracker.Kind))
            {
                errors.Add(new GameCatalogValidationError(dataset, "InvalidValue", $"'{label}' has unrecognized tracker kind '{tracker.Kind}'."));
            }

            if (tracker.Points <= 0)
            {
                errors.Add(new GameCatalogValidationError(dataset, "InvalidValue", $"'{label}' has a '{tracker.Kind}' tracker with {tracker.Points} points."));
            }
        }
    }
}
