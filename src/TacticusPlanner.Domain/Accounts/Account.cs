using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Domain.Accounts;

public class Account : BaseEntity<AccountId>
{
    public required string Issuer { get; set; }

    public required string Subject { get; set; }

    /// <summary>When this account was last seen making an authenticated request (updated on every
    /// <c>GET /me</c> call). Used as the "last active in Planner" signal for guild member rosters.</summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    public virtual Profile? Profile { get; set; }
}
