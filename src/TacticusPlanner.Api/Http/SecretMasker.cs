namespace TacticusPlanner.Api.Http;

/// <summary>
/// Produces a display-safe partial preview of a sensitive value (Tacticus API key, Tacticus user id) for API
/// responses. Only this masked preview ever leaves the server — endpoints must never return the source value.
/// </summary>
internal static class SecretMasker
{
    private const int VisibleSuffixLength = 4;
    private const int MaskedPrefixLength = 8;

    public static string? Mask(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.Length <= VisibleSuffixLength)
        {
            return new string('•', value.Length);
        }

        return new string('•', MaskedPrefixLength) + value[^VisibleSuffixLength..];
    }
}
