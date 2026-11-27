using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

/// <summary>One ability's entry in the curated <c>unit-abilities</c> file.</summary>
public sealed record GameCatalogCuratedAbility(IReadOnlyList<string> Roles, string Target);

internal static partial class GameCatalogDenormalizer
{
    /// <summary>
    /// Reads the curated <c>unit-abilities</c> file (unit id to ability id to roles and target) into the served
    /// abilities of every lineup unit: a character's active then passive abilities, a MoW's primary then secondary,
    /// each with the datamine attack range (<c>Melee</c> or <c>Ranged</c>; <c>Normal</c> and none serve null).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A curated id is not a character or MoW, a unit's curated abilities differ from its datamine active and passive
    /// abilities, a role is unknown or missing, or a target is unknown, so a datamine bump that adds a unit or
    /// changes its abilities fails until they are curated.
    /// </exception>
    public static IReadOnlyDictionary<string, IReadOnlyList<GameCatalogUnitAbility>> ReadUnitAbilities(
        IReadOnlyDictionary<string, Dictionary<string, GameCatalogCuratedAbility>> curated,
        IReadOnlyDictionary<string, DatamineLineupUnit> lineup,
        IReadOnlyDictionary<string, DatamineAbility?> abilities)
    {
        var errors = new List<string>();
        errors.AddRange(curated.Keys
            .Where(unitId => !lineup.ContainsKey(unitId))
            .Order(StringComparer.Ordinal)
            .Select(unitId => $"'{unitId}' is not a character or MoW"));

        var served = new Dictionary<string, IReadOnlyList<GameCatalogUnitAbility>>(StringComparer.Ordinal);
        foreach (var (unitId, unit) in lineup.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var kinds = AbilityKinds(unit);
            if (!curated.TryGetValue(unitId, out var entries))
            {
                errors.Add($"'{unitId}' has no curated abilities");
                continue;
            }

            errors.AddRange(kinds
                .Where(pair => !entries.ContainsKey(pair.Id))
                .Select(pair => $"'{unitId}' ability '{pair.Id}' is not curated"));
            errors.AddRange(entries.Keys
                .Where(id => kinds.All(pair => pair.Id != id))
                .Order(StringComparer.Ordinal)
                .Select(id => $"'{unitId}' ability '{id}' is curated but not one of its abilities"));
            foreach (var (id, entry) in entries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (entry.Roles is not { Count: > 0 } || entry.Roles.Any(role => !GameCatalogAbilityRoles.All.Contains(role))
                    || entry.Roles.Distinct(StringComparer.Ordinal).Count() != entry.Roles.Count)
                {
                    errors.Add(
                        $"'{unitId}' ability '{id}' has roles [{string.Join(", ", entry.Roles ?? [])}] " +
                        "(expected one or more of 'damage', 'support', 'utility')");
                }

                if (!GameCatalogAbilityTargets.All.Contains(entry.Target ?? ""))
                {
                    errors.Add($"'{unitId}' ability '{id}' has target '{entry.Target}' (expected 'none', 'single' or 'aoe')");
                }
            }

            served[unitId] = kinds
                .Where(pair => entries.ContainsKey(pair.Id))
                .Select(pair => new GameCatalogUnitAbility(
                    pair.Id,
                    pair.Kind,
                    entries[pair.Id].Roles,
                    entries[pair.Id].Target,
                    AttackOf(abilities.GetValueOrDefault(pair.Id))))
                .ToArray();
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"GameCatalog dataset '{GameCatalogDatasets.UnitAbilities}': {string.Join("; ", errors)}.");
        }

        return served;
    }

    private static List<(string Id, string Kind)> AbilityKinds(DatamineLineupUnit unit) =>
        unit.Traits.Contains(MachineOfWarTraitId, StringComparer.Ordinal)
            ? unit.ActiveAbilities.Select((id, index) => (id, index == 0 ? "primary" : "secondary")).ToList()
            :
            [
                .. unit.ActiveAbilities.Select(id => (id, "active")),
                .. (unit.PassiveAbilities ?? []).Select(id => (id, "passive")),
            ];

    private static string? AttackOf(DatamineAbility? ability) => ability?.AttackRangeType switch
    {
        "Melee" => "melee",
        "Ranged" => "ranged",
        _ => null,
    };
}
