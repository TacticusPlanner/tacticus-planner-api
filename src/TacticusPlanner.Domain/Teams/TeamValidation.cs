using System.Globalization;

namespace TacticusPlanner.Domain.Teams;

/// <summary>Limits of a <see cref="Team"/> (<c>personal-teams</c>, "Names, notes and team count are limited").</summary>
public static class TeamValidation
{
    public const int MinNameLength = 3;

    public const int MaxNameLength = 40;

    public const int MaxNotesLength = 300;

    /// <summary>The default per-profile team limit; the API reads the configured value (<c>TeamOptions.Limit</c>).</summary>
    public const int DefaultLimit = 50;

    /// <summary>Fewest characters a team has, in every mode; MoWs do not count.</summary>
    public const int MinCharacters = 3;

    /// <summary>Most war and Tournament Arena core characters.</summary>
    public const int MaxWarCoreCharacters = 5;

    /// <summary>Most war and Tournament Arena core MoWs; flex MoWs do not count.</summary>
    public const int MaxWarCoreMows = 1;

    /// <summary>Generous bounds on list sizes, so a request cannot carry an unbounded array.</summary>
    public const int MaxCharacters = 30;

    public const int MaxMows = 10;

    public const int MaxIncursionMows = 20;

    public const int MaxUnitIdLength = 64;

    public const int MaxImportKeyLength = 512;

    /// <summary>The case-insensitive key that keeps names unique within a profile.</summary>
    public static string NameKey(string name) => name.Trim().ToLower(CultureInfo.InvariantCulture);

    /// <summary>Blank notes are stored as null; others are trimmed.</summary>
    public static string? NormalizeNotes(string? notes) => string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
}
