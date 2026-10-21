using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Api.Features.Analytics;

/// <summary>
/// Derives a stable, pseudonymous analytics id for an account (its profile — one profile per account):
/// <c>lowercase_hex(HMAC-SHA256(IdentityKey, "&lt;destination&gt;:" + profileId))</c>. The profile id is a v7
/// GUID whose first 48 bits are its creation timestamp in plaintext (RFC 9562); keying the digest and
/// label-separating it per destination is what keeps the result from disclosing that timestamp, the
/// account's creation order, or letting two destinations be joined on the same id. See
/// specs/analytics-identity/spec.md.
/// </summary>
public sealed class AnalyticsIdentityDeriver(IOptions<AnalyticsOptions> options)
{
    /// <summary>The only destination this change derives ids for. Part of the derivation itself (not
    /// configuration) — see design.md's "destination label is part of the derivation" risk.</summary>
    public const string PostHogDestination = "posthog";

    public string Derive(ProfileId profileId, string destination)
    {
        if (!options.Value.TryDecodeIdentityKey(out var key))
        {
            throw new InvalidOperationException("Analytics:IdentityKey is not a valid 32-byte base64 key.");
        }

        var message = Encoding.UTF8.GetBytes($"{destination}:{profileId.Value}");
        var hash = HMACSHA256.HashData(key, message);
        return Convert.ToHexStringLower(hash);
    }
}
