using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.Persistence.Model;

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
}
