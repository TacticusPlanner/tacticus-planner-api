using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.Persistence.Model;
using TacticusPlanner.Persistence.Users.PlayerData;

namespace TacticusPlanner.Persistence.Users;

public class Profile : BaseEntity<ProfileId>
{
    public AccountId AccountId { get; set; }

    public required string DisplayName { get; set; }

    [Encrypted]
    public string? TacticusUserId { get; set; }

    public byte[]? TacticusUserIdHash { get; set; }

    public virtual Account? Account { get; set; }

    public virtual TacticusIntegration? TacticusIntegration { get; set; }

    public virtual PlayerDataSnapshot? PlayerDataSnapshot { get; set; }

    public virtual PlayerDataOverride? PlayerDataOverride { get; set; }
}
