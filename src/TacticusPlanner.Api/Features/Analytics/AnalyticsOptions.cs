namespace TacticusPlanner.Api.Features.Analytics;

public sealed class AnalyticsOptions
{
    public const string SectionName = "Analytics";

    private const int IdentityKeySize = 32;

    /// <summary>PostHog project token. Absent is the supported way to run with capture switched off —
    /// unlike <see cref="IdentityKey"/>, this is not validated on start.</summary>
    public string? ProjectToken { get; set; }

    public string? HostUrl { get; set; }

    /// <summary>Base64-encoded 32-byte HMAC key deriving every account's pseudonymous analytics id.
    /// Required — the current-user response's analytics id must always be derivable.</summary>
    public string IdentityKey { get; set; } = string.Empty;

    internal bool TryDecodeIdentityKey(out byte[] key)
    {
        try
        {
            key = Convert.FromBase64String(IdentityKey);
            return key.Length == IdentityKeySize;
        }
        catch (FormatException)
        {
            key = [];
            return false;
        }
    }
}
