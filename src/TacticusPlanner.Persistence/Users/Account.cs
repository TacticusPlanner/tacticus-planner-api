using TacticusPlanner.Persistence.Model;

namespace TacticusPlanner.Persistence.Users;

public class Account : BaseEntity<AccountId>
{
    public required string Issuer { get; set; }

    public required string Subject { get; set; }

    public virtual Profile? Profile { get; set; }
}
