using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace TacticusPlanner.Persistence.Encryption;

/// <summary>
/// AES-256-GCM column encryption. Writes the version-2 envelope
/// <c>tpenc:2:{keyVersion}:{nonce}:{tag}:{ciphertext}</c>, whose associated data is the column name, so a
/// ciphertext copied into another encrypted column fails to decrypt.
/// </summary>
/// <remarks>The binding is to the column, not the row: EF value converters never see the row they convert.</remarks>
public sealed class AesGcmColumnEncryptionService(IOptions<ColumnEncryptionOptions> options)
    : IColumnEncryptionService
{
    /// <summary>Every envelope starts with this.</summary>
    public const string CurrentEnvelopePrefix = "tpenc:2:";

    private const string Prefix = "tpenc";
    private const string CurrentEnvelopeVersion = "2";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public string? Encrypt(string? plaintext, string column)
    {
        if (plaintext is null)
        {
            return null;
        }

        if (plaintext.StartsWith($"{Prefix}:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Encrypted column plaintext must not start with the encryption envelope prefix.");
        }

        var keyVersion = options.Value.GetCurrentKeyVersion();
        var key = options.Value.GetKey(keyVersion);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, Encoding.UTF8.GetBytes(column));

        return string.Join(
            ':',
            Prefix,
            CurrentEnvelopeVersion,
            keyVersion,
            EncodeBase64Url(nonce),
            EncodeBase64Url(tag),
            EncodeBase64Url(ciphertext)
        );
    }

    public string? Decrypt(string? envelope, string column)
    {
        if (envelope is null)
        {
            return null;
        }

        var parts = envelope.Split(':');
        if (parts.Length != 6 || parts[0] != Prefix || parts[1] != CurrentEnvelopeVersion)
        {
            throw new InvalidOperationException("Encrypted column value is not a valid encryption envelope.");
        }

        var key = options.Value.GetKey(parts[2]);
        var nonce = DecodeBase64Url(parts[3], NonceSize, "nonce");
        var tag = DecodeBase64Url(parts[4], TagSize, "tag");
        var ciphertext = DecodeBase64Url(parts[5], expectedLength: null, "ciphertext");
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(column));

        return Encoding.UTF8.GetString(plaintext);
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
