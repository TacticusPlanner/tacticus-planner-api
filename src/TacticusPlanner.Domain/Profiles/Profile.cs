using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.PlayerData;
using UserSettingsEntity = TacticusPlanner.Domain.UserSettings.UserSettings;

namespace TacticusPlanner.Domain.Profiles;

public class Profile : BaseEntity<ProfileId>
{
    /// <summary>Identity-provider issuer of the signed-in user; with <see cref="Subject"/> it uniquely
    /// identifies the caller (one profile per account).</summary>
    public required string Issuer { get; set; }

    public required string Subject { get; set; }

    /// <summary>When this profile was last seen making an authenticated request (updated by <c>GET /account</c>).
    /// Used as the "last active in Planner" signal for guild member rosters.</summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    public required string DisplayName { get; set; }

    public TacticusUserId? TacticusUserId { get; set; }

    public TacticusUserIdHash? TacticusUserIdHash { get; set; }

    /// <summary>Optimistic-concurrency token for the account-wide goal order: advanced whenever the set or
    /// order of in-flight goals changes, and checked by the reorder operations.</summary>
    public long GoalOrderRevision { get; set; }

    public virtual TacticusIntegration? TacticusIntegration { get; set; }

    public virtual PlayerDataSnapshot? PlayerDataSnapshot { get; set; }

    public virtual PlayerDataOverride? PlayerDataOverride { get; set; }

    public virtual UserSettingsEntity? UserSettings { get; set; }
}
