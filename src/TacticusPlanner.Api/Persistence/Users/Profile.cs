using TacticusPlanner.Api.Persistence.Encryption;
using TacticusPlanner.Api.Persistence.Model;

namespace TacticusPlanner.Api.Persistence.Users;

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
