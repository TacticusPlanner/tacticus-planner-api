using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace TacticusPlanner.Persistence.Encryption;

public sealed class AesGcmColumnEncryptionService(IOptions<ColumnEncryptionOptions> options)
    : IColumnEncryptionService
{
    private const string Prefix = "tpenc";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    public string? Encrypt(string? plaintext)
    {
        if (plaintext is null)
        {
            return null;
        }

        if (plaintext.StartsWith($"{Prefix}:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Encrypted column plaintext must not start with the encryption envelope prefix.");
        }

        var keyVersion = options.Value.CurrentKeyVersion;
        if (string.IsNullOrWhiteSpace(keyVersion))
        {
            throw new InvalidOperationException("ColumnEncryption:CurrentKeyVersion is not configured.");
        }

        var key = GetKey(keyVersion);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        return string.Join(
            ':',
            Prefix,
            keyVersion,
            EncodeBase64Url(nonce),
            EncodeBase64Url(tag),
            EncodeBase64Url(ciphertext)
        );
    }

    public string? Decrypt(string? envelope)
    {
        if (envelope is null)
        {
            return null;
        }

        var parts = envelope.Split(':');
        if (parts.Length != 5 || parts[0] != Prefix)
        {
            throw new InvalidOperationException("Encrypted column value is not a valid encryption envelope.");
        }

        var key = GetKey(parts[1]);
        var nonce = DecodeBase64Url(parts[2], NonceSize, "nonce");
        var tag = DecodeBase64Url(parts[3], TagSize, "tag");
        var ciphertext = DecodeBase64Url(parts[4], expectedLength: null, "ciphertext");
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return System.Text.Encoding.UTF8.GetString(plaintext);
    }

    private byte[] GetKey(string keyVersion)
    {
        if (!options.Value.Keys.TryGetValue(keyVersion, out var encodedKey)
            || string.IsNullOrWhiteSpace(encodedKey))
        {
            throw new InvalidOperationException($"ColumnEncryption:Keys:{keyVersion} is not configured.");
        }

        var key = DecodeConfiguredKey(encodedKey);
        if (key.Length != KeySize)
        {
            throw new InvalidOperationException($"ColumnEncryption:Keys:{keyVersion} must be a 32-byte AES-256 key.");
        }

        return key;
    }

    private static byte[] DecodeConfiguredKey(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return DecodeBase64Url(value, expectedLength: null, "key");
        }
    }

    private static string EncodeBase64Url(byte[] value)
    {
        return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] DecodeBase64Url(string value, int? expectedLength, string partName)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(padded);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException($"Encrypted column {partName} is not valid base64url.", exception);
        }

        if (expectedLength is not null && decoded.Length != expectedLength)
        {
            throw new InvalidOperationException($"Encrypted column {partName} length is invalid.");
        }

        return decoded;
    }
}
