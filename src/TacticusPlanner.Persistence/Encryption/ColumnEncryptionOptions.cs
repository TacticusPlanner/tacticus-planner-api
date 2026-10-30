namespace TacticusPlanner.Persistence.Encryption;

public sealed class ColumnEncryptionOptions
{
    public const string SectionName = "ColumnEncryption";

    public const string ValidationFailureMessage =
        "ColumnEncryption:CurrentKeyVersion must name a configured key, and every ColumnEncryption:Keys entry "
        + "must be a 32-byte base64 key.";

    private const int KeySize = 32;

    public string? CurrentKeyVersion { get; set; }

    public Dictionary<string, string> Keys { get; set; } = [];

    /// <summary>The startup check: the current version names a key and every configured key (older versions
    /// still decrypt existing rows) decodes to 32 bytes.</summary>
    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(CurrentKeyVersion)
        && Keys.ContainsKey(CurrentKeyVersion)
        && Keys.Values.All(value => TryDecodeKey(value, out _));

    public string GetCurrentKeyVersion() =>
        string.IsNullOrWhiteSpace(CurrentKeyVersion)
            ? throw new InvalidOperationException("ColumnEncryption:CurrentKeyVersion is not configured.")
            : CurrentKeyVersion;

    public byte[] GetKey(string keyVersion)
    {
        if (!Keys.TryGetValue(keyVersion, out var encodedKey) || string.IsNullOrWhiteSpace(encodedKey))
        {
            throw new InvalidOperationException($"ColumnEncryption:Keys:{keyVersion} is not configured.");
        }

        return TryDecodeKey(encodedKey, out var key)
            ? key
            : throw new InvalidOperationException($"ColumnEncryption:Keys:{keyVersion} must be a 32-byte key.");
    }

    private static bool TryDecodeKey(string value, out byte[] key)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        try
        {
            key = Convert.FromBase64String(padded);
            return key.Length == KeySize;
        }
        catch (FormatException)
        {
            key = [];
            return false;
        }
    }
}
