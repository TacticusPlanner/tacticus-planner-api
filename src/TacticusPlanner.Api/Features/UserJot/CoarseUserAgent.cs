using System.Text.RegularExpressions;

namespace TacticusPlanner.Api.Features.UserJot;

/// <summary>
/// Reduces a <c>User-Agent</c> header to a browser family with its major version ("Chrome 131") and an OS family
/// ("Android"), for feedback triage. The full header is never kept: it adds fingerprinting detail without helping
/// triage. OS versions are left out because browsers freeze them in the header (Chrome reports Android 10 and
/// macOS 10.15.7 on every device). Rules run in order because most browsers also claim to be Chrome and Safari.
/// </summary>
public static partial class CoarseUserAgent
{
    private static readonly (Regex Pattern, string Name)[] Browsers =
    [
        (SamsungInternet(), "Samsung Internet"),
        (Edge(), "Edge"),
        (Opera(), "Opera"),
        (Firefox(), "Firefox"),
        (Chrome(), "Chrome"),
        (Safari(), "Safari"),
    ];

    private static readonly (string Marker, string Name)[] OperatingSystems =
    [
        ("Windows NT", "Windows"),
        ("Android", "Android"),
        ("iPhone", "iOS"),
        ("iPod", "iOS"),
        ("iPad", "iPadOS"),
        ("CrOS", "ChromeOS"),
        ("Macintosh", "macOS"),
        ("Linux", "Linux"),
    ];

    public static string? Browser(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
        {
            return null;
        }

        foreach (var (pattern, name) in Browsers)
        {
            var match = pattern.Match(userAgent);
            if (match.Success)
            {
                return $"{name} {match.Groups["major"].Value}";
            }
        }

        return null;
    }

    public static string? OperatingSystem(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
        {
            return null;
        }

        foreach (var (marker, name) in OperatingSystems)
        {
            if (userAgent.Contains(marker, StringComparison.Ordinal))
            {
                return name;
            }
        }

        return null;
    }

    [GeneratedRegex(@"SamsungBrowser/(?<major>\d{1,4})")]
    private static partial Regex SamsungInternet();

    [GeneratedRegex(@"Edg(?:e|A|iOS)?/(?<major>\d{1,4})")]
    private static partial Regex Edge();

    [GeneratedRegex(@"(?:OPR|OPT|OPiOS)/(?<major>\d{1,4})")]
    private static partial Regex Opera();

    [GeneratedRegex(@"(?:Firefox|FxiOS)/(?<major>\d{1,4})")]
    private static partial Regex Firefox();

    [GeneratedRegex(@"(?:Chrome|CriOS)/(?<major>\d{1,4})")]
    private static partial Regex Chrome();

    [GeneratedRegex(@"Version/(?<major>\d{1,4})[^ ]* (?:Mobile/\S+ )?Safari/")]
    private static partial Regex Safari();
}
