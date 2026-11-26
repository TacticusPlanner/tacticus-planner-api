using System.Text.RegularExpressions;

namespace TacticusPlanner.Api.Features.UserJot;

/// <summary>
/// Builds the flat, non-identifying <c>traits</c> claim of the UserJot identity token: context that helps triage
/// feedback (which build, installed app or browser, UI language, whether game sync is linked). The client-supplied
/// values are only echoed when they match a strict shape, so a stray or hostile value is dropped instead of
/// reaching UserJot; UserJot ignores nested objects and arrays in traits, so every value is a scalar.
/// </summary>
public static partial class UserJotTraits
{
    public const string AppVersion = "appVersion";
    public const string AppMode = "appMode";
    public const string Language = "language";
    public const string TacticusApiLinked = "tacticusApiLinked";

    private static readonly HashSet<string> AppModes = new(StringComparer.Ordinal) { "installed", "browser" };

    public static Dictionary<string, object> Build(
        string? appVersion,
        string? appMode,
        string? language,
        bool tacticusApiLinked
    )
    {
        var traits = new Dictionary<string, object> { [TacticusApiLinked] = tacticusApiLinked };

        if (appVersion is not null && AppVersionPattern().IsMatch(appVersion))
        {
            traits[AppVersion] = appVersion;
        }

        if (appMode is not null && AppModes.Contains(appMode))
        {
            traits[AppMode] = appMode;
        }

        if (language is not null && LanguagePattern().IsMatch(language))
        {
            traits[Language] = language;
        }

        return traits;
    }

    // A release tag ("v2.6.0"), a version ("2.6.0-rc.1+abc") or a commit SHA.
    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z.+_-]{0,39}$")]
    private static partial Regex AppVersionPattern();

    // A BCP 47 primary language with an optional region ("en", "pt-BR").
    [GeneratedRegex("^[a-z]{2,3}(-[A-Z]{2})?$")]
    private static partial Regex LanguagePattern();
}
