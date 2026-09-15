using System.Globalization;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private static void ValidateGuildRaidMeta(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors)
    {
        var characterIds = new HashSet<string>(snapshot.Characters.Select(character => character.Id), StringComparer.Ordinal);
        var mowIds = new HashSet<string>(snapshot.Mows.Select(mow => mow.Id), StringComparer.Ordinal);
        var bossIds = new HashSet<string>(snapshot.RaidBossesView.Bosses.Select(boss => boss.UnitSetId), StringComparer.Ordinal);
        var primeIds = new HashSet<string>(snapshot.RaidBossesView.Primes.Select(prime => prime.UnitSetId), StringComparer.Ordinal);

        ValidateGuildRaidMeta(snapshot.GuildRaidMetaRawData, snapshot.GuildRaidMetaView, characterIds, mowIds, bossIds, primeIds, errors);
    }

    internal static void ValidateGuildRaidMeta(
        GameCatalogGuildRaidMetaRawData raw,
        GameCatalogGuildRaidMetaView view,
        HashSet<string> characterIds,
        HashSet<string> mowIds,
        HashSet<string> bossIds,
        HashSet<string> primeIds,
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
        ValidateUniqueValues(dataset, "prime unit-set id", raw.Primes.Select(prime => prime.PrimeUnitSetId), errors);
        ValidateUniqueValues(
            dataset,
            "recommendation id",
            raw.Bosses.SelectMany(boss => boss.Recommendations)
                .Concat(raw.Primes.SelectMany(prime => prime.Recommendations))
                .Select(recommendation => recommendation.Id),
            errors);

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

            foreach (var primeUnitSetId in boss.PrimeUnitSetIds)
            {
                RequireReference(dataset, boss.BossUnitSetId, "primeUnitSetIds", primeUnitSetId, primeIds, errors);
            }

            RequireNonEmpty(dataset, boss.Recommendations.Count, errors);
            ValidateUniqueValues(
                dataset, $"recommendation kind for '{boss.BossUnitSetId}'", boss.Recommendations.Select(recommendation => recommendation.Kind), errors);

            foreach (var recommendation in boss.Recommendations)
            {
                ValidateGuildRaidMetaRecommendation($"{boss.BossUnitSetId}/{recommendation.Kind}", recommendation, characterIds, mowIds, compIds, errors);
            }
        }

        foreach (var prime in raw.Primes)
        {
            Require(dataset, "prime group", prime.PrimeUnitSetId, "primeUnitSetId", errors);
            if (!primeIds.Contains(prime.PrimeUnitSetId))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, "MissingReference", $"Prime group '{prime.PrimeUnitSetId}' does not resolve to a served prime."));
            }

            RequireNonEmpty(dataset, prime.Recommendations.Count, errors);
            ValidateUniqueValues(
                dataset, $"recommendation kind for '{prime.PrimeUnitSetId}'", prime.Recommendations.Select(recommendation => recommendation.Kind), errors);

            foreach (var recommendation in prime.Recommendations)
            {
                ValidateGuildRaidMetaRecommendation($"{prime.PrimeUnitSetId}/{recommendation.Kind}", recommendation, characterIds, mowIds, compIds, errors);
            }
        }
    }

    private static void ValidateGuildRaidMetaRecommendation(
        string owner,
        GameCatalogGuildRaidMetaRawRecommendation recommendation,
        HashSet<string> characterIds,
        HashSet<string> mowIds,
        HashSet<string> compIds,
        List<GameCatalogValidationError> errors)
    {
        const string dataset = GameCatalogDatasets.GuildRaidMeta;

        Require(dataset, owner, recommendation.Kind, "kind", errors);
        Require(dataset, owner, recommendation.Id, "id", errors);

        if (recommendation.Efficiency <= 0)
        {
            errors.Add(new GameCatalogValidationError(
                dataset, "InvalidEfficiency", $"Recommendation '{owner}' has non-positive efficiency '{recommendation.Efficiency}'."));
        }

        ValidateMowReferences(owner, "mowId", [recommendation.MowId], mowIds, errors);

        if (recommendation.HeroSlots.Count != 5)
        {
            errors.Add(new GameCatalogValidationError(
                dataset, "InvalidHeroSlotCount", $"Recommendation '{owner}' must contain exactly five hero slots."));
        }

        ValidateUniqueValues(
            dataset,
            $"heroId for '{owner}'",
            recommendation.HeroSlots.Select(slot => slot.HeroId),
            errors);

        for (var slotIndex = 0; slotIndex < recommendation.HeroSlots.Count; slotIndex++)
        {
            var slot = recommendation.HeroSlots[slotIndex];
            var slotOwner = $"{owner}[{slotIndex}]";

            Require(dataset, slotOwner, slot.RoleId, "roleId", errors);

            ValidateCharacterReferences(slotOwner, "heroId", [slot.HeroId], characterIds, errors);
            ValidateCharacterReferences(slotOwner, "replacementCharacterIds", slot.ReplacementCharacterIds, characterIds, errors);

            if (slot.ReplacementCharacterIds.Contains(slot.HeroId))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset,
                    "SelfReplacement",
                    $"Recommendation '{slotOwner}' replacementCharacterIds must not include its own heroId '{slot.HeroId}'."));
            }
        }

        ValidateMowReferences(owner, "mowReplacementIds", recommendation.MowReplacementIds, mowIds, errors);
        if (recommendation.MowReplacementIds.Contains(recommendation.MowId))
        {
            errors.Add(new GameCatalogValidationError(
                dataset,
                "SelfReplacement",
                $"Recommendation '{owner}' mowReplacementIds must not include its own mowId '{recommendation.MowId}'."));
        }

        RequireNonEmpty(dataset, recommendation.CompIds.Count, errors);
        ValidateUniqueValues(dataset, $"comp id for '{owner}'", recommendation.CompIds, errors);
        foreach (var compId in recommendation.CompIds)
        {
            RequireReference(dataset, owner, "compIds", compId, compIds, errors);
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
