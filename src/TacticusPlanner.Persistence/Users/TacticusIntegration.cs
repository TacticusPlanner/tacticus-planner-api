using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.Persistence.Model;

namespace TacticusPlanner.Persistence.Users;

public class TacticusIntegration : BaseEntity<ProfileId>
{
    [Encrypted]
    public string? TacticusApiKey { get; set; }

    public DateTimeOffset? TacticusSyncLastAttemptedAt { get; set; }

    public DateTimeOffset? TacticusSyncLastSucceededAt { get; set; }

    public virtual Profile? Profile { get; set; }
}
