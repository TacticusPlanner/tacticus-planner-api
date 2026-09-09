using System.Globalization;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private static readonly HashSet<string> ValidGuildRaidMetaRecommendationKinds =
        new(StringComparer.Ordinal) { "meta", "alternate" };

    private static void ValidateGuildRaidMeta(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors)
    {
        var characterIds = new HashSet<string>(snapshot.Characters.Select(character => character.Id), StringComparer.Ordinal);
        var mowIds = new HashSet<string>(snapshot.Mows.Select(mow => mow.Id), StringComparer.Ordinal);
        var bossIds = new HashSet<string>(snapshot.RaidBossesView.Bosses.Select(boss => boss.UnitSetId), StringComparer.Ordinal);

        ValidateGuildRaidMeta(snapshot.GuildRaidMetaRawData, snapshot.GuildRaidMetaView, characterIds, mowIds, bossIds, errors);
    }

    internal static void ValidateGuildRaidMeta(
        GameCatalogGuildRaidMetaRawData raw,
        GameCatalogGuildRaidMetaView view,
        HashSet<string> characterIds,
        HashSet<string> mowIds,
        HashSet<string> bossIds,
        List<GameCatalogValidationError> errors)
    {
        const string dataset = GameCatalogDatasets.GuildRaidMeta;

        Require(dataset, "dataset", raw.SourceId, "sourceId", errors);
        if (!DateOnly.TryParseExact(raw.UpdatedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            errors.Add(new GameCatalogValidationError(dataset, "InvalidDate", "'updatedOn' must be an ISO-8601 calendar date."));
        }

        RequireNonEmpty(dataset, raw.Comps.Count, errors);
        RequireNonEmpty(dataset, raw.Bosses.Count, errors);
        RequireNonEmpty(dataset, view.Comps.Count, errors);
        RequireNonEmpty(dataset, view.Bosses.Count, errors);

        ValidateUniqueValues(dataset, "comp id", raw.Comps.Select(comp => comp.Id), errors);
        ValidateUniqueValues(dataset, "boss unit-set id", raw.Bosses.Select(boss => boss.BossUnitSetId), errors);

        var compIds = new HashSet<string>(raw.Comps.Select(comp => comp.Id), StringComparer.Ordinal);
        foreach (var comp in raw.Comps)
        {
            Require(dataset, "comp", comp.Id, "id", errors);
            Require(dataset, comp.Id, comp.SignatureUnitId, "signatureUnitId", errors);
            if (!characterIds.Contains(comp.SignatureUnitId) && !mowIds.Contains(comp.SignatureUnitId))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, "MissingReference", $"Comp '{comp.Id}' has unresolved signatureUnitId '{comp.SignatureUnitId}'."));
            }

            ValidateCharacterReferences(comp.Id, "coreCharacterIds", comp.CoreCharacterIds, characterIds, errors);
            ValidateCharacterReferences(comp.Id, "flexCharacterIds", comp.FlexCharacterIds, characterIds, errors);
            ValidateMowReferences(comp.Id, "mowIds", comp.MowIds, mowIds, errors);
        }

        foreach (var boss in raw.Bosses)
        {
            Require(dataset, "boss group", boss.BossUnitSetId, "bossUnitSetId", errors);
            if (!bossIds.Contains(boss.BossUnitSetId))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, "MissingReference", $"Boss group '{boss.BossUnitSetId}' does not resolve to a served boss."));
            }

            RequireNonEmpty(dataset, boss.Recommendations.Count, errors);
            ValidateUniqueValues(dataset, $"recommendation kind for '{boss.BossUnitSetId}'", boss.Recommendations.Select(recommendation => recommendation.Kind), errors);

            foreach (var recommendation in boss.Recommendations)
            {
                var owner = $"{boss.BossUnitSetId}/{recommendation.Kind}";
                if (!ValidGuildRaidMetaRecommendationKinds.Contains(recommendation.Kind))
                {
                    errors.Add(new GameCatalogValidationError(
                        dataset, "InvalidRecommendationKind", $"Recommendation '{owner}' has invalid kind '{recommendation.Kind}'."));
                }

                if (recommendation.HeroIds.Count != 5)
                {
                    errors.Add(new GameCatalogValidationError(
                        dataset, "InvalidHeroCount", $"Recommendation '{owner}' must contain exactly five hero ids."));
                }

                ValidateCharacterReferences(owner, "heroIds", recommendation.HeroIds, characterIds, errors);
                ValidateMowReferences(owner, "mowId", [recommendation.MowId], mowIds, errors);

                RequireNonEmpty(dataset, recommendation.CompIds.Count, errors);
                ValidateUniqueValues(dataset, $"comp id for '{owner}'", recommendation.CompIds, errors);
                foreach (var compId in recommendation.CompIds)
                {
                    RequireReference(dataset, owner, "compIds", compId, compIds, errors);
                }

                if (recommendation.Evidence is { } evidence
                    && (evidence.ReplayCount < 0 || evidence.AverageDamage < 0 || evidence.MaximumDamage < 0))
                {
                    errors.Add(new GameCatalogValidationError(
                        dataset, "InvalidEvidence", $"Recommendation '{owner}' has negative replay evidence."));
                }
            }
        }
    }

    private static void ValidateCharacterReferences(
        string owner,
        string field,
        IReadOnlyList<string> ids,
        HashSet<string> characterIds,
        List<GameCatalogValidationError> errors)
    {
        const string dataset = GameCatalogDatasets.GuildRaidMeta;
        ValidateUniqueValues(dataset, $"{field} for '{owner}'", ids, errors);
        foreach (var id in ids)
        {
            RequireReference(dataset, owner, field, id, characterIds, errors);
        }
    }

    private static void ValidateMowReferences(
        string owner,
        string field,
        IReadOnlyList<string> ids,
        HashSet<string> mowIds,
        List<GameCatalogValidationError> errors)
    {
        const string dataset = GameCatalogDatasets.GuildRaidMeta;
        ValidateUniqueValues(dataset, $"{field} for '{owner}'", ids, errors);
        foreach (var id in ids)
        {
            RequireReference(dataset, owner, field, id, mowIds, errors);
        }
    }

    private static void ValidateUniqueValues(
        string dataset,
        string field,
        IEnumerable<string> values,
        List<GameCatalogValidationError> errors)
    {
        var duplicate = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            errors.Add(new GameCatalogValidationError(
                dataset, "DuplicateId", $"Duplicate {field} '{duplicate.Key}'."));
        }
    }
}
