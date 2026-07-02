using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace TacticusPlanner.Api.Persistence.Encryption;

public sealed class HmacColumnHashService(IOptions<ColumnEncryptionOptions> options)
    : IColumnHashService
{
    private const int KeySize = 32;

    public byte[]? ComputeHash(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var keyVersion = options.Value.CurrentKeyVersion;
        if (string.IsNullOrWhiteSpace(keyVersion))
        {
            throw new InvalidOperationException("ColumnEncryption:CurrentKeyVersion is not configured.");
        }

        if (!options.Value.Keys.TryGetValue(keyVersion, out var encodedKey)
            || string.IsNullOrWhiteSpace(encodedKey))
        {
            throw new InvalidOperationException($"ColumnEncryption:Keys:{keyVersion} is not configured.");
        }

        var key = DecodeConfiguredKey(encodedKey);
        if (key.Length != KeySize)
        {
            throw new InvalidOperationException($"ColumnEncryption:Keys:{keyVersion} must be a 32-byte key.");
        }

        return HMACSHA256.HashData(key, System.Text.Encoding.UTF8.GetBytes(value));
    }

    private static byte[] DecodeConfiguredKey(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
            return Convert.FromBase64String(padded);
        }
    }
}
