using System.Globalization;
using System.Security.Cryptography;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence.Encryption;

namespace TacticusPlanner.Api.Features.StagingTransfer;

/// <summary>
/// Mints and reads the short-lived transfer code a tester copies from production into staging. The code is
/// the production profile id and an expiry, sealed with production's column-encryption key (AES-256-GCM),
/// so staging, which holds only the shared service key, can neither read nor forge one. Nothing is stored:
/// a code is valid until it expires, for any number of redemptions within that window.
/// </summary>
public sealed class StagingTransferCodeService(IColumnEncryptionService encryption, TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>Bound into the ciphertext so no stored column value can be replayed as a code.</summary>
    private const string Purpose = "staging_transfer.code";
    private const string PayloadVersion = "1";
    private const string CodePrefix = "TP1-";

    public (string Code, DateTimeOffset ExpiresAt) Mint(ProfileId profileId)
    {
        var expiresAt = timeProvider.GetUtcNow().Add(Lifetime);
        var payload = string.Join(
            '|',
            PayloadVersion,
            profileId.Value.ToString("N"),
            expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        var envelope = encryption.Encrypt(payload, Purpose)!;
        return (CodePrefix + envelope[AesGcmColumnEncryptionService.CurrentEnvelopePrefix.Length..], expiresAt);
    }

    /// <summary>The production profile the code was minted for, or null when it is malformed, tampered
    /// with, sealed with another key, or expired.</summary>
    public ProfileId? Read(string? code)
    {
        var trimmed = code?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !trimmed.StartsWith(CodePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string? payload;
        try
        {
            payload = encryption.Decrypt(
                AesGcmColumnEncryptionService.CurrentEnvelopePrefix + trimmed[CodePrefix.Length..],
                Purpose);
        }
        catch (Exception exception) when (exception is InvalidOperationException or CryptographicException
            or ArgumentException or KeyNotFoundException)
        {
            return null;
        }

        var parts = payload?.Split('|');
        if (parts is not [PayloadVersion, var id, var expiry]
            || !Guid.TryParseExact(id, "N", out var profileGuid)
            || !long.TryParse(expiry, NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAtSeconds))
        {
            return null;
        }

        return timeProvider.GetUtcNow().ToUnixTimeSeconds() < expiresAtSeconds ? ProfileId.From(profileGuid) : null;
    }
}
