using TacticusPlanner.Persistence.Users;

namespace TacticusPlanner.Api.Features.Auth;

/// <summary>
/// Per-request identity, resolved once by <see cref="CurrentUserPreProcessor"/> and read by every
/// authenticated endpoint via <c>ProcessorState&lt;CurrentUserState&gt;()</c> instead of each endpoint
/// re-extracting the <c>iss</c>/<c>sub</c> claims and re-querying the Account/Profile ids itself.
/// </summary>
public sealed class CurrentUserState
{
    public string Issuer { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    /// <summary>Null until the caller has an Account row — i.e. before their first call to GetCurrentUser
    /// provisions one.</summary>
    public AccountId? AccountId { get; set; }

    /// <summary>Null until the caller has a Profile row — see <see cref="AccountId"/>.</summary>
    public ProfileId? ProfileId { get; set; }

    public bool IsProvisioned => ProfileId is not null;
}
