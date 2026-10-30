using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace TacticusPlanner.Persistence.Encryption;

/// <summary>HMAC-SHA256 keyed hash for lookup columns. The HMAC key is an HKDF-SHA256 subkey of the current
/// column-encryption key, so the AES key itself is never reused for hashing.</summary>
public sealed class HmacColumnHashService(IOptions<ColumnEncryptionOptions> options)
    : IColumnHashService
{
    private const int KeySize = 32;

    private static readonly byte[] HashKeyInfo = "tacticus-planner/column-hash/v1"u8.ToArray();

    public byte[]? ComputeHash(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var masterKey = options.Value.GetKey(options.Value.GetCurrentKeyVersion());
        var hashKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, KeySize, salt: [], info: HashKeyInfo);
        return HMACSHA256.HashData(hashKey, Encoding.UTF8.GetBytes(value));
    }
}
