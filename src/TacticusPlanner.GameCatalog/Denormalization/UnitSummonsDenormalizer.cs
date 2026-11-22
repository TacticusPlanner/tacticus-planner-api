using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    // An ability that brings a unit onto the battlefield carries the summon's stat table under this variable
    // name (summonHp, summonHp_2, ...).
    private const string SummonHealthVariablePrefix = "summonHp";

    /// <summary>
    /// Reads the curated <c>unit-summons</c> file (unit id to <c>optional</c> or <c>forced</c>) into the summon type
    /// of every lineup unit: listed units keep their curated type, every other unit is <c>none</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A curated id is not a character or MoW, a type is not <c>optional</c> or <c>forced</c>, or the curated units
    /// differ from the lineup units with an ability that has summon stats (a <c>summonHp…</c> variable), so a
    /// datamine bump with a new summoner fails until its type is curated.
    /// </exception>
    public static IReadOnlyDictionary<string, string> ReadUnitSummons(
        IReadOnlyDictionary<string, string> curated,
        IReadOnlyDictionary<string, DatamineLineupUnit> lineup,
        IReadOnlyDictionary<string, DatamineAbility?> abilities)
    {
        var errors = new List<string>();
        foreach (var (unitId, summons) in curated)
        {
            if (!lineup.ContainsKey(unitId))
            {
                errors.Add($"'{unitId}' is not a character or MoW");
            }

            if (summons is not (GameCatalogUnitSummons.Optional or GameCatalogUnitSummons.Forced))
            {
                errors.Add($"'{unitId}' has summons '{summons}' (expected '{GameCatalogUnitSummons.Optional}' or '{GameCatalogUnitSummons.Forced}')");
            }
        }

        var summoners = lineup
            .Where(pair => pair.Value.ActiveAbilities
                .Concat(pair.Value.PassiveAbilities ?? [])
                .Any(ability => HasSummonStats(abilities.GetValueOrDefault(ability))))
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);
        errors.AddRange(summoners
            .Where(unitId => !curated.ContainsKey(unitId))
            .Order(StringComparer.Ordinal)
            .Select(unitId => $"'{unitId}' has an ability with summon stats but no curated summons"));
        errors.AddRange(curated.Keys
            .Where(unitId => lineup.ContainsKey(unitId) && !summoners.Contains(unitId))
            .Order(StringComparer.Ordinal)
            .Select(unitId => $"'{unitId}' is curated but has no ability with summon stats"));

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"GameCatalog dataset '{GameCatalogDatasets.UnitSummons}': {string.Join("; ", errors)}.");
        }

        return lineup.Keys.ToDictionary(
            unitId => unitId,
            unitId => curated.GetValueOrDefault(unitId, GameCatalogUnitSummons.None),
            StringComparer.Ordinal);
    }

    private static bool HasSummonStats(DatamineAbility? ability) =>
        ability?.Variables?.Keys.Any(name => name.StartsWith(SummonHealthVariablePrefix, StringComparison.Ordinal)) == true;
}
