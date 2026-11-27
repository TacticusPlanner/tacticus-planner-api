using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Features.V1Import;

/// <summary>
/// Resolves a V1 unit reference onto a catalog character or MoW id: the id itself, one of V1's hard-coded renames,
/// or a catalog character name (V1's legacy <c>charactersIds</c> held names). Shared by the Legendary Event and team imports so both resolve units identically
/// (<c>add-personal-teams</c> design D7).
/// </summary>
internal sealed class UnitResolver(GameCatalogSnapshot catalog)
{
    /// <summary>V1 <c>CharactersService.canonicalName</c>'s hard-coded renames.</summary>
    private static readonly Dictionary<string, string> UnitAliases = new(StringComparer.Ordinal)
    {
        ["Sho'Syl"] = "tauMarksman",
        ["Re'Vas"] = "tauCrisis",
        ["PoM"] = "tyranParasite",
        ["Abaddon The Despoiler"] = "blackAbaddon",
        ["Winged Tyrant Prime"] = "tyranWingedPrime",
        ["Tan Gi'Da"] = "admecMarshall",
        ["Nauseous Rotbone"] = "deathRotbone",
        ["Sy-Gex"] = "admecDestroyer",
        ["Patermine"] = "genesPatriarch",
    };

    private readonly HashSet<string> ids = catalog.CharacterViews.Select(character => character.Id)
        .Concat(catalog.MowList.Select(mow => mow.Id))
        .ToHashSet(StringComparer.Ordinal);

    private readonly Dictionary<string, string> byName = catalog.CharacterViews
        .GroupBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);

    public string? Resolve(string reference)
    {
        if (ids.Contains(reference))
            return reference;
        if (UnitAliases.TryGetValue(reference, out var alias) && ids.Contains(alias))
            return alias;
        return byName.GetValueOrDefault(reference.Trim());
    }

    /// <summary>For matching unit sets before lane checks: the resolved id, or the reference itself.</summary>
    public string Canonical(string reference) => Resolve(reference) ?? reference;
}
