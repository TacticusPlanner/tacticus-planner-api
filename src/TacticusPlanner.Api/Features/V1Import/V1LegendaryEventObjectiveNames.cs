using System.Text;
using System.Text.RegularExpressions;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Features.V1Import;

/// <summary>
/// Regenerates the display name V1 gave a Legendary Event objective (V1 <c>objectiveDisplayName</c>) from the
/// catalog's structural filter, so a V1 team's <c>restrictionsIds</c> (names, never indexes) can be matched
/// to the catalog objective regardless of casing, whitespace and the known legacy spellings. The catalog's
/// own name is always a candidate too, so a hand-authored catalog name still resolves.
/// </summary>
public static partial class V1LegendaryEventObjectiveNames
{
    // V1's Trait enum: key → display value, where the two differ from a plain word split.
    private static readonly Dictionary<string, string> TraitNames = new(StringComparer.Ordinal)
    {
        ["BeastSnagga"] = "Beast Slayer",
        ["TeleportStrike"] = "Deep Strike",
        ["FinalJustice"] = "Final Vengeance",
        ["MartialKatah"] = "Martial Ka'tah",
        ["MkXGravis"] = "MK X Gravis",
        ["TwoManTeam"] = "Two-Man Team",
        ["WeaverOfFate"] = "Weaver of Fates",
        ["ActOfFaith"] = "Act of Faith",
        ["BlessingsOfKhorne"] = "Blessings of Khorne",
        ["ContagionsOfNurgle"] = "Contagions of Nurgle",
        ["LetTheGalaxyBurn"] = "Let the Galaxy Burn",
        ["ShadowInTheWarp"] = "Shadow in the Warp",
        ["TerminatorArmour"] = "Terminator Armour",
    };

    // V1's factions.json: snowprint id → display name, where the two differ from a plain word split.
    private static readonly Dictionary<string, string> FactionNames = new(StringComparer.Ordinal)
    {
        ["Sisterhood"] = "Adepta Sororitas",
        ["Tau"] = "T'au Empire",
        ["Genestealers"] = "Genestealer Cults",
        ["Custodes"] = "Adeptus Custodes",
        ["EmperorsChildren"] = "Emperor's Children",
        ["LeaguesOfVotann"] = "Leagues of Votann",
        ["TheLostAndTheDamned"] = "The Lost and the Damned",
    };

    private static readonly Dictionary<string, string> DamageTypeNames = new(StringComparer.Ordinal)
    {
        ["HeavyRound"] = "Heavy Round",
    };

    // V1 NAME_OVERRIDES: a couple of traits have a shorter display name than their enum value.
    private static readonly Dictionary<(string Kind, string Target, bool Exclude), string> Overrides = new()
    {
        [("Trait", "TerminatorArmour", false)] = "Terminator",
        [("Trait", "TerminatorArmour", true)] = "No Terminator",
    };

    // Legacy hand-authored spellings seen in V1 team data, applied after normalisation.
    private static readonly (string From, string To)[] Aliases =
    [
        ("resiliant", "resilient"),
        ("no range", "no ranged"),
    ];

    /// <summary>The V1 display name for a catalog objective filter.</summary>
    public static string V1DisplayName(GameCatalogLreFilter filter)
    {
        if (Overrides.TryGetValue((filter.Kind, filter.Target, filter.Exclude), out var overridden))
        {
            return overridden;
        }

        var name = filter.Kind switch
        {
            "Trait" => TraitNames.GetValueOrDefault(filter.Target) ?? SplitWords(filter.Target),
            "Faction" => FactionNames.GetValueOrDefault(filter.Target) ?? SplitWords(filter.Target),
            "DamageType" => DamageTypeNames.GetValueOrDefault(filter.Target) ?? filter.Target,
            "Alliance" => filter.Target,
            "MinHits" => $"Min {filter.Target} hits",
            "MaxHits" => $"Max {filter.Target} hits",
            "AttackType" => filter.Exclude ? "Melee" : "Ranged",
            "NoSummons" => "No Summons",
            _ => string.IsNullOrEmpty(filter.Target) ? filter.Kind : $"{filter.Kind} {filter.Target}",
        };

        return filter.Exclude && filter.Kind is "Trait" or "Faction" or "DamageType" or "Alliance"
            ? $"No {name}"
            : name;
    }

    /// <summary>Every normalised name <paramref name="objective"/> may be referred to by: the regenerated
    /// V1 display name and the catalog name.</summary>
    public static IReadOnlySet<string> Candidates(GameCatalogLreRestriction objective) =>
        new HashSet<string>([Normalize(V1DisplayName(objective.Filter)), Normalize(objective.Name)], StringComparer.Ordinal);

    /// <summary>The lane objective a V1 restriction name refers to, or null.</summary>
    public static GameCatalogLreRestriction? Resolve(GameCatalogLreTrackView lane, string? restrictionName)
    {
        if (string.IsNullOrWhiteSpace(restrictionName))
        {
            return null;
        }

        var wanted = Normalize(restrictionName);
        return lane.UnitsRestrictions.FirstOrDefault(objective => Candidates(objective).Contains(wanted));
    }

    /// <summary>Lower-cased, whitespace-collapsed, with the legacy aliases applied.</summary>
    public static string Normalize(string name)
    {
        var normalized = WhitespaceRun().Replace(name.Trim(), " ").ToLowerInvariant();
        foreach (var (from, to) in Aliases)
        {
            normalized = normalized.Replace(from, to, StringComparison.Ordinal);
        }

        return normalized;
    }

    private static string SplitWords(string pascalCase)
    {
        var builder = new StringBuilder(pascalCase.Length + 4);
        for (var index = 0; index < pascalCase.Length; index++)
        {
            var character = pascalCase[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(pascalCase[index - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
