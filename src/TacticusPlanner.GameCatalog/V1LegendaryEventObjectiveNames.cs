using System.Globalization;
using System.Text;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog;

/// <summary>
/// V1 identifies LRE objectives by display name. This regenerates the name V1's <c>objectiveDisplayName</c>
/// would give a catalog objective (V1 <c>objective-dispatch.ts</c>) so a V1 <c>restrictionsIds</c> entry can be
/// matched to the lane objective's <c>index</c>. Matching is case-insensitive after whitespace normalisation and
/// also accepts the catalog name and the legacy hand-typed spellings V1 events carried.
/// </summary>
public static class V1LegendaryEventObjectiveNames
{
    /// <summary>V1's <c>Trait</c> enum labels where they differ from a plain split of the snowprint key.</summary>
    private static readonly Dictionary<string, string> TraitLabels = new(StringComparer.Ordinal)
    {
        ["ActOfFaith"] = "Act of Faith",
        ["BeastSnagga"] = "Beast Slayer",
        ["BlessingsOfKhorne"] = "Blessings of Khorne",
        ["ContagionsOfNurgle"] = "Contagions of Nurgle",
        ["TeleportStrike"] = "Deep Strike",
        ["FinalJustice"] = "Final Vengeance",
        ["LetTheGalaxyBurn"] = "Let the Galaxy Burn",
        ["MartialKatah"] = "Martial Ka'tah",
        ["MkXGravis"] = "MK X Gravis",
        ["ShadowInTheWarp"] = "Shadow in the Warp",
        ["TerminatorArmour"] = "Terminator Armour",
        ["TwoManTeam"] = "Two-Man Team",
        ["WeaverOfFate"] = "Weaver of Fates",
    };

    /// <summary>V1's <c>NAME_OVERRIDES</c>: shorter names for a couple of traits.</summary>
    private static readonly Dictionary<(string Kind, string Target, bool Exclude), string> Overrides = new()
    {
        [("Trait", "TerminatorArmour", false)] = "Terminator",
        [("Trait", "TerminatorArmour", true)] = "No Terminator",
    };

    /// <summary>Legacy V1 spellings, compared after normalisation.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["resiliant"] = "resilient",
        ["no resiliant"] = "no resilient",
        ["no range"] = "no ranged",
    };

    /// <summary>The name V1 gives this objective.</summary>
    public static string DisplayName(GameCatalogLreFilter filter)
    {
        if (Overrides.TryGetValue((filter.Kind, filter.Target, filter.Exclude), out var overridden))
            return overridden;

        var name = filter.Kind switch
        {
            "Trait" => TraitLabels.GetValueOrDefault(filter.Target) ?? SplitWords(filter.Target),
            "Faction" or "Alliance" or "DamageType" => SplitWords(filter.Target),
            "MinHits" => $"Min {filter.Target} hits",
            "MaxHits" => $"Max {filter.Target} hits",
            // V1 HasRangedAttack / HasNoRangedAttack.
            "AttackType" when filter.Target == "Ranged" => filter.Exclude ? "Melee" : "Ranged",
            "AttackType" => filter.Exclude ? "Ranged" : "Melee",
            _ => $"{filter.Kind} {filter.Target}".Trim(),
        };
        var negatable = filter.Kind is "Trait" or "Faction" or "Alliance" or "DamageType";
        return negatable && filter.Exclude ? $"No {name}" : name;
    }

    /// <summary>True when a V1 objective name refers to this catalog objective.</summary>
    public static bool Matches(string v1Name, GameCatalogLreRestriction restriction)
    {
        var normalized = Normalize(v1Name);
        return normalized.Length > 0
            && (normalized == Normalize(DisplayName(restriction.Filter))
                || normalized == Normalize(restriction.Name)
                // Legacy V1 events wrote melee-only as "No Range" (aliased to "No Ranged" above).
                || (restriction.Filter is { Kind: "AttackType", Target: "Ranged", Exclude: true } && normalized == "no ranged"));
    }

    /// <summary>Lower-case, single-spaced, legacy spellings replaced.</summary>
    public static string Normalize(string name)
    {
        var collapsed = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLower(CultureInfo.InvariantCulture);
        return Aliases.GetValueOrDefault(collapsed) ?? collapsed;
    }

    private static string SplitWords(string pascal)
    {
        var builder = new StringBuilder(pascal.Length + 4);
        for (var index = 0; index < pascal.Length; index++)
        {
            if (index > 0 && char.IsUpper(pascal[index]) && !char.IsUpper(pascal[index - 1]))
                builder.Append(' ');
            builder.Append(pascal[index]);
        }

        return builder.ToString();
    }
}
