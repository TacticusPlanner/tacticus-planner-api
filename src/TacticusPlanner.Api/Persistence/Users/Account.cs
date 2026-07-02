using TacticusPlanner.Api.Persistence.Model;

namespace TacticusPlanner.Api.Persistence.Users;

public class Account : BaseEntity<AccountId>
{
    public required string Issuer { get; set; }

    public required string Subject { get; set; }

    public virtual Profile? Profile { get; set; }
}
