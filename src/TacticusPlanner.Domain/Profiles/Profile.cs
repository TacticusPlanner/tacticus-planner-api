using TacticusPlanner.Domain.Accounts;
using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.PlayerData;

namespace TacticusPlanner.Domain.Profiles;

public class Profile : BaseEntity<ProfileId>
{
    public AccountId AccountId { get; set; }

    public required string DisplayName { get; set; }

    public TacticusUserId? TacticusUserId { get; set; }

    public byte[]? TacticusUserIdHash { get; set; }

    public virtual Account? Account { get; set; }

    public virtual TacticusIntegration? TacticusIntegration { get; set; }

    public virtual PlayerDataSnapshot? PlayerDataSnapshot { get; set; }

    public virtual PlayerDataOverride? PlayerDataOverride { get; set; }
}
